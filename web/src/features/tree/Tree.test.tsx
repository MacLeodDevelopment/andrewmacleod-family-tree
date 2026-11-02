import { fireEvent, screen, within } from '@testing-library/react'
import { http, HttpResponse } from 'msw'
import { describe, expect, it } from 'vitest'
import { fakeAuth, renderApp } from '../../test/renderApp.tsx'
import { server } from '../../test/server.ts'
import type { Graph } from './graph.ts'

const signedIn = fakeAuth({ user: { name: 'Jean', username: 'jean@example.test' } })

const morag = { id: 'morag', name: 'Morag MacLeod', born: 'c. 1870', died: '1901', isHalfSibling: false }
const anna = { id: 'anna', name: 'Anna MacLeod', born: null, died: null, isHalfSibling: false }
const rory = { id: 'rory', name: 'Rory MacLeod', born: null, died: null, isHalfSibling: true }

const graph: Graph = {
  focusId: 'morag',
  people: [morag, anna, rory],
  families: [
    {
      id: 'f',
      partner1Id: 'anna',
      partner2Id: null,
      type: 'None',
      children: [{ childId: 'morag', partner1Relationship: 'Biological', partner2Relationship: 'Unknown' }],
    },
    {
      id: 'g',
      partner1Id: 'anna',
      partner2Id: null,
      type: 'None',
      children: [{ childId: 'rory', partner1Relationship: 'Biological', partner2Relationship: 'Unknown' }],
    },
  ],
}

function signedInWithRoles(...roles: string[]) {
  server.use(http.get('*/api/v1/me', () => HttpResponse.json({ name: 'Jean', username: 'jean@example.test', roles })))
}

function graphIs(value: Graph) {
  server.use(http.get(`*/api/v1/people/${value.focusId}/graph`, () => HttpResponse.json(value)))
}

async function openTree() {
  signedInWithRoles('Family.Viewer')
  graphIs(graph)
  renderApp(signedIn, '/tree/morag')
  await screen.findByRole('heading', { level: 1, name: 'Morag MacLeod' })
}

describe('Opening the tree from the menu', () => {
  function linkIs(link: { personId: string; isApproved: boolean } | null) {
    server.use(http.get('*/api/v1/me/link', () => (link ? HttpResponse.json(link) : new HttpResponse(null, { status: 204 }))))
  }

  it('is offered to every member, linked or not', async () => {
    signedInWithRoles('Family.Admin')
    linkIs(null)
    renderApp(signedIn)

    expect(await screen.findByRole('link', { name: 'Tree' })).toHaveAttribute('href', '/tree')
  })

  it('is not offered to people without a family role', async () => {
    server.use(http.get('*/api/v1/me', () => new HttpResponse(null, { status: 403 })))
    renderApp(signedIn)

    await screen.findByText('No access')
    expect(screen.queryByRole('link', { name: 'Tree' })).not.toBeInTheDocument()
  })

  it('asks visitors to sign in', () => {
    renderApp(fakeAuth(), '/tree')

    expect(screen.getByText('Please sign in to see this page.')).toBeInTheDocument()
  })

  it('opens on you once your account is linked', async () => {
    signedInWithRoles('Family.Viewer')
    linkIs({ personId: 'morag', isApproved: true })
    graphIs(graph)
    renderApp(signedIn, '/tree')

    expect(await screen.findByRole('heading', { level: 1, name: 'Morag MacLeod' })).toBeInTheDocument()
  })

  it.each([
    ['not linked', null],
    ['still waiting for approval', { personId: 'morag', isApproved: false }],
  ])('asks who to start from when you are %s', async (_, link) => {
    signedInWithRoles('Family.Viewer')
    linkIs(link)
    server.use(http.get('*/api/v1/people/search', () => HttpResponse.json({ items: [morag], total: 1 })))
    renderApp(signedIn, '/tree')

    expect(await screen.findByRole('heading', { level: 1, name: 'Who should the tree start from?' })).toBeInTheDocument()
    fireEvent.change(screen.getByRole('searchbox', { name: 'Name' }), { target: { value: 'Morag' } })

    expect(await screen.findByRole('link', { name: 'Morag MacLeod' })).toHaveAttribute('href', '/tree/morag')
  })
})

describe('The family tree', () => {
  it('asks visitors to sign in', () => {
    renderApp(fakeAuth(), '/tree/morag')

    expect(screen.getByText('Please sign in to see this page.')).toBeInTheDocument()
  })

  it('keeps out people without a family role', async () => {
    server.use(http.get('*/api/v1/me', () => new HttpResponse(null, { status: 403 })))

    renderApp(signedIn, '/tree/morag')

    expect(await screen.findByRole('alert')).toHaveTextContent("You don't have access to this page.")
  })

  it('shows the focus person and their relatives, with dates', async () => {
    await openTree()

    const focus = screen.getByRole('link', { name: 'Morag MacLeod, b. c. 1870, d. 1901' })
    expect(focus).toHaveAttribute('aria-current', 'true')
    expect(screen.getByRole('link', { name: 'Anna MacLeod' })).not.toHaveAttribute('aria-current')
  })

  it('puts each event on a line of its own', async () => {
    await openTree()

    const focus = within(screen.getByRole('link', { name: 'Morag MacLeod, b. c. 1870, d. 1901' }))
    expect(focus.getByText('b. c. 1870')).toBeInTheDocument()
    expect(focus.getByText('d. 1901')).toBeInTheDocument()
  })

  it('moves to whoever is chosen', async () => {
    await openTree()
    graphIs({ ...graph, focusId: 'anna' })

    fireEvent.click(screen.getByRole('link', { name: 'Anna MacLeod' }))

    expect(await screen.findByRole('heading', { level: 1, name: 'Anna MacLeod' })).toBeInTheDocument()
  })

  it('shows an unknown parent as Unknown, not as a gap', async () => {
    await openTree()

    expect(within(screen.getByRole('group', { name: 'Family tree' })).getAllByText('Unknown')).toHaveLength(2)
  })

  it('marks half-siblings', async () => {
    await openTree()

    const rory = screen.getByRole('link', { name: 'Rory MacLeod, half-sibling' })
    expect(within(rory).getByText('half')).toBeInTheDocument()
  })

  it('shortens a very long name but keeps it whole for tooltips and screen readers', async () => {
    const long = 'Marion Catriona Elizabeth MacLeod-Campbell'
    signedInWithRoles('Family.Viewer')
    graphIs({ focusId: 'long', people: [{ ...anna, id: 'long', name: long }], families: [] })
    renderApp(signedIn, '/tree/long')

    const card = await screen.findByRole('link', { name: long })
    expect(within(card).getByText(/…$/)).toBeInTheDocument()
    expect(card.querySelector('title')).toHaveTextContent(long)
  })

  it('explains the lines', async () => {
    await openTree()

    const legend = within(screen.getByRole('list', { name: 'What the lines mean' }))
    expect(legend.getByText('Adopted, step or foster')).toBeInTheDocument()
    expect(legend.getByText('Presumed or unknown')).toBeInTheDocument()
  })

  it('links to the profile of the focus person', async () => {
    await openTree()

    expect(screen.getByRole('link', { name: 'See profile' })).toHaveAttribute('href', '/family/morag')
  })

  it('says so when the person does not exist', async () => {
    signedInWithRoles('Family.Viewer')
    server.use(http.get('*/api/v1/people/missing/graph', () => new HttpResponse(null, { status: 404 })))

    renderApp(signedIn, '/tree/missing')

    expect(await screen.findByRole('alert')).toHaveTextContent("This person doesn't exist.")
  })

  it('says so when the tree cannot be loaded', async () => {
    signedInWithRoles('Family.Viewer')
    server.use(http.get('*/api/v1/people/morag/graph', () => new HttpResponse(null, { status: 500 })))

    renderApp(signedIn, '/tree/morag')

    expect(await screen.findByRole('alert')).toHaveTextContent("The tree couldn't be loaded.")
  })
})
