import { fireEvent, screen, waitFor, within } from '@testing-library/react'
import { http, HttpResponse } from 'msw'
import { describe, expect, it } from 'vitest'
import { fakeAuth, renderApp } from '../../test/renderApp.tsx'
import { server } from '../../test/server.ts'
import type { PersonSummary, Profile } from './profile.ts'

const signedIn = fakeAuth({ user: { name: 'Jean', username: 'jean@example.test' } })

const summary = (id: string, name: string, born: string | null = null, died: string | null = null): PersonSummary => ({
  id,
  name,
  born,
  died,
})

const morag = summary('morag', 'Morag MacLeod', 'c. 1870', '1901')
const anna = summary('anna', 'Anna MacLeod')
const flora = summary('flora', 'Flora MacLeod')
const rory = summary('rory', 'Rory MacLeod')
const ishbel = summary('ishbel', 'Ishbel Campbell')
const calum = summary('calum', 'Calum MacLeod')

const profile: Profile = {
  id: 'morag',
  isPrivate: false,
  names: [
    { givenName: 'Morag', surname: 'MacLeod', type: 'Birth' },
    { givenName: 'Morag', surname: 'Campbell', type: 'Married' },
  ],
  events: [{ type: 'Birth', date: 'c. 1870', place: 'Innellan' }],
  parents: [
    { person: anna, relationship: 'Adopted' },
    { person: null, relationship: 'Unknown' },
  ],
  siblings: [
    { person: flora, isHalf: false },
    { person: rory, isHalf: true },
  ],
  partnerships: [
    {
      partner: ishbel,
      type: 'Marriage',
      events: [{ type: 'Marriage', date: '1895', place: 'Dunoon' }],
      children: [{ person: calum, relationship: 'Step' }],
    },
    { partner: null, type: 'None', events: [], children: [] },
  ],
}

function signedInWithRoles(...roles: string[]) {
  server.use(http.get('*/api/v1/me', () => HttpResponse.json({ name: 'Jean', username: 'jean@example.test', roles })))
}

function profileIs(value: Profile) {
  server.use(http.get(`*/api/v1/people/${value.id}`, () => HttpResponse.json(value)))
}

describe('Who can search and read profiles', () => {
  it('asks visitors to sign in', () => {
    renderApp(fakeAuth(), '/search')

    expect(screen.getByText('Please sign in to see this page.')).toBeInTheDocument()
  })

  it('keeps out people without a family role', async () => {
    server.use(http.get('*/api/v1/me', () => new HttpResponse(null, { status: 403 })))

    renderApp(signedIn, '/family/morag')

    expect(await screen.findByRole('alert')).toHaveTextContent("You don't have access to this page.")
    expect(screen.queryByRole('link', { name: 'Search' })).not.toBeInTheDocument()
  })

  it('gives viewers a link to search, but not to the admin screens', async () => {
    signedInWithRoles('Family.Viewer')

    renderApp(signedIn)

    expect(await screen.findByRole('link', { name: 'Search' })).toHaveAttribute('href', '/search')
    expect(screen.queryByRole('link', { name: 'People' })).not.toBeInTheDocument()
  })
})

describe('Searching', () => {
  it('finds people by the name typed and shows their dates', async () => {
    signedInWithRoles('Family.Viewer')
    let asked: string | null = null
    server.use(
      http.get('*/api/v1/people/search', ({ request }) => {
        asked = new URL(request.url).searchParams.get('q')
        return HttpResponse.json({ items: [morag], total: 1 })
      }),
    )
    renderApp(signedIn, '/search')

    fireEvent.change(await screen.findByLabelText('Name'), { target: { value: ' Morag Mac ' } })

    const result = await screen.findByRole('link', { name: 'Morag MacLeod' })
    expect(result).toHaveAttribute('href', '/family/morag')
    expect(result.closest('li')).toHaveTextContent('b. c. 1870, d. 1901')
    expect(asked).toBe('Morag Mac')
  })

  it('says so when nobody matches', async () => {
    signedInWithRoles('Family.Viewer')
    server.use(http.get('*/api/v1/people/search', () => HttpResponse.json({ items: [], total: 0 })))
    renderApp(signedIn, '/search')

    fireEvent.change(await screen.findByLabelText('Name'), { target: { value: 'Nobody' } })

    expect(await screen.findByText('Nobody found.')).toBeInTheDocument()
  })

  it('says when there are more results than it shows', async () => {
    signedInWithRoles('Family.Viewer')
    server.use(http.get('*/api/v1/people/search', () => HttpResponse.json({ items: [morag], total: 21 })))
    renderApp(signedIn, '/search')

    fireEvent.change(await screen.findByLabelText('Name'), { target: { value: 'Mac' } })

    expect(await screen.findByText(/Showing 1 of 21/)).toBeInTheDocument()
  })

  it('says so when the search fails', async () => {
    signedInWithRoles('Family.Viewer')
    server.use(http.get('*/api/v1/people/search', () => new HttpResponse(null, { status: 500 })))
    renderApp(signedIn, '/search')

    fireEvent.change(await screen.findByLabelText('Name'), { target: { value: 'Mac' } })

    expect(await screen.findByRole('alert')).toHaveTextContent("The search didn't work.")
  })

  it('waits for at least two letters', async () => {
    signedInWithRoles('Family.Viewer')
    let searched = false
    server.use(
      http.get('*/api/v1/people/search', () => {
        searched = true
        return HttpResponse.json({ items: [], total: 0 })
      }),
    )
    renderApp(signedIn, '/search')

    fireEvent.change(await screen.findByLabelText('Name'), { target: { value: 'M' } })
    await new Promise((resolve) => setTimeout(resolve, 350))

    expect(searched).toBe(false)
  })
})

describe('Saying which person you are', () => {
  const open = async () => {
    signedInWithRoles('Family.Viewer')
    profileIs(profile)
    renderApp(signedIn, '/family/morag')
    await screen.findByRole('heading', { level: 1, name: 'Morag MacLeod' })
  }

  const linkIs = (personId: string, isApproved: boolean) =>
    server.use(http.get('*/api/v1/me/link', () => HttpResponse.json({ personId, isApproved })))

  it('sends the request and then says it is waiting', async () => {
    let asked = false
    await open()
    server.use(
      http.post('*/api/v1/people/morag/claim', () => {
        asked = true
        linkIs('morag', false)
        return HttpResponse.json({ personId: 'morag', isApproved: false })
      }),
    )

    fireEvent.click(await screen.findByRole('button', { name: 'This is me' }))

    expect(await screen.findByText('Waiting for an admin to confirm this is you.')).toBeInTheDocument()
    expect(asked).toBe(true)
  })

  it('can ask for someone else while still waiting', async () => {
    linkIs('someone-else', false)
    await open()

    expect(await screen.findByRole('button', { name: 'This is me' })).toBeInTheDocument()
  })

  it('has no page of their own until approved', async () => {
    linkIs('morag', false)
    await open()

    await screen.findByText('Waiting for an admin to confirm this is you.')
    expect(screen.queryByRole('link', { name: 'My page' })).not.toBeInTheDocument()
  })

  it('confirms it on their own page, and links to it from the header', async () => {
    linkIs('morag', true)
    await open()

    expect(await screen.findByText('This is you.')).toBeInTheDocument()
    expect(screen.getByRole('link', { name: 'My page' })).toHaveAttribute('href', '/family/morag')
  })

  it("doesn't offer it on other people's pages once linked", async () => {
    linkIs('someone-else', true)
    await open()

    await screen.findByRole('link', { name: 'My page' })
    expect(screen.queryByRole('button', { name: 'This is me' })).not.toBeInTheDocument()
  })

  it("doesn't call anyone's page theirs before they're linked", async () => {
    await open()

    await screen.findByRole('button', { name: 'This is me' })
    expect(screen.queryByRole('link', { name: 'My page' })).not.toBeInTheDocument()
  })

  it('says why when the request is refused', async () => {
    await open()
    server.use(
      http.post('*/api/v1/people/morag/claim', () =>
        HttpResponse.json({ title: 'This account is already linked' }, { status: 409, headers: { 'Content-Type': 'application/problem+json' } }),
      ),
    )

    fireEvent.click(await screen.findByRole('button', { name: 'This is me' }))

    expect(await screen.findByRole('alert')).toHaveTextContent('This account is already linked')
  })
})

describe('Linked accounts', () => {
  const pending = {
    accountId: 'a1',
    displayName: 'Jean Campbell',
    username: 'jean@example.test',
    isApproved: false,
    personId: 'morag',
    personName: 'Morag MacLeod',
  }

  const linksAre = (...links: (typeof pending)[]) =>
    server.use(http.get('*/api/v1/admin/account-links', () => HttpResponse.json(links)))

  it('is closed to viewers', async () => {
    signedInWithRoles('Family.Viewer')

    renderApp(signedIn, '/people/accounts')

    expect(await screen.findByRole('alert')).toHaveTextContent("You don't have access to this page.")
  })

  it('says so when nobody has asked', async () => {
    signedInWithRoles('Family.Admin')
    linksAre()

    renderApp(signedIn, '/people/accounts')

    expect(await screen.findByText('Nobody has asked to be linked yet.')).toBeInTheDocument()
  })

  it('shows who is asking, and approves them', async () => {
    let approved = false
    signedInWithRoles('Family.Admin')
    linksAre(pending)
    server.use(
      http.post('*/api/v1/admin/account-links/a1/approve', () => {
        approved = true
        linksAre({ ...pending, isApproved: true })
        return new HttpResponse(null, { status: 204 })
      }),
    )
    renderApp(signedIn, '/people/accounts')

    const row = (await screen.findByRole('link', { name: 'Morag MacLeod' })).closest('li')!
    expect(row).toHaveTextContent('Jean Campbell (jean@example.test) says they are Morag MacLeod')
    fireEvent.click(screen.getByRole('button', { name: 'Approve Jean Campbell' }))

    await waitFor(() => expect(row).toHaveTextContent('Jean Campbell (jean@example.test) is Morag MacLeod'))
    expect(approved).toBe(true)
    await waitFor(() => expect(screen.queryByRole('button', { name: 'Approve Jean Campbell' })).not.toBeInTheDocument())
  })

  it('removes a link after confirming', async () => {
    let removed = false
    signedInWithRoles('Family.Admin')
    linksAre({ ...pending, isApproved: true })
    server.use(
      http.delete('*/api/v1/admin/account-links/a1', () => {
        removed = true
        linksAre()
        return new HttpResponse(null, { status: 204 })
      }),
    )
    renderApp(signedIn, '/people/accounts')

    fireEvent.click(await screen.findByRole('button', { name: 'Remove Jean Campbell' }))
    expect(removed).toBe(false)
    fireEvent.click(screen.getByRole('button', { name: 'Yes, remove' }))

    expect(await screen.findByText('Nobody has asked to be linked yet.')).toBeInTheDocument()
  })

  it('says so when an approval fails', async () => {
    signedInWithRoles('Family.Admin')
    linksAre(pending)
    server.use(http.post('*/api/v1/admin/account-links/a1/approve', () => new HttpResponse(null, { status: 500 })))
    renderApp(signedIn, '/people/accounts')

    fireEvent.click(await screen.findByRole('button', { name: 'Approve Jean Campbell' }))

    expect(await screen.findByRole('alert')).toHaveTextContent("This couldn't be approved.")
  })

  it('says so when the list cannot be loaded', async () => {
    signedInWithRoles('Family.Admin')
    server.use(http.get('*/api/v1/admin/account-links', () => new HttpResponse(null, { status: 500 })))

    renderApp(signedIn, '/people/accounts')

    expect(await screen.findByRole('alert')).toHaveTextContent("The linked accounts couldn't be loaded.")
  })
})

describe('A profile', () => {
  async function openProfile(...roles: string[]) {
    signedInWithRoles(...(roles.length ? roles : ['Family.Viewer']))
    profileIs(profile)
    renderApp(signedIn, '/family/morag')
    await screen.findByRole('heading', { level: 1, name: 'Morag MacLeod' })
  }

  const row = (name: string) => screen.getByRole('link', { name }).closest('li')

  it('shows their other names and life events', async () => {
    await openProfile()

    expect(screen.getByText(/Also known as Morag Campbell \(married\)/)).toBeInTheDocument()
    expect(screen.getByText(/c\. 1870 · Innellan/)).toBeInTheDocument()
  })

  it('shows parents, including an unknown one, and how they are related', async () => {
    await openProfile()

    expect(row('Anna MacLeod')).toHaveTextContent('(adopted)')
    expect(within(row('Anna MacLeod')!.closest('ul')!).getByText('Unknown')).toBeInTheDocument()
  })

  it('tells half-brothers and half-sisters from full ones', async () => {
    await openProfile()

    expect(row('Rory MacLeod')).toHaveTextContent('(half)')
    expect(row('Flora MacLeod')).not.toHaveTextContent('(half)')
  })

  it('shows each partnership with its marriage and children', async () => {
    await openProfile()

    expect(screen.getByRole('heading', { level: 3, name: /With Ishbel Campbell · Marriage/ })).toBeInTheDocument()
    expect(screen.getByRole('heading', { level: 3, name: 'With someone unknown' })).toBeInTheDocument()
    expect(screen.getByText(/1895 · Dunoon/)).toBeInTheDocument()
    expect(row('Calum MacLeod')).toHaveTextContent('(step)')
  })

  it('moves to a relative when their name is clicked', async () => {
    await openProfile()
    profileIs({ ...profile, id: 'anna', names: [{ givenName: 'Anna', surname: 'MacLeod', type: 'Birth' }] })

    fireEvent.click(screen.getByRole('link', { name: 'Anna MacLeod' }))

    expect(await screen.findByRole('heading', { level: 1, name: 'Anna MacLeod' })).toBeInTheDocument()
  })

  it('says so when nothing is recorded', async () => {
    signedInWithRoles('Family.Viewer')
    profileIs({ ...profile, events: [], parents: [], siblings: [], partnerships: [] })
    renderApp(signedIn, '/family/morag')

    expect(await screen.findByText('No family recorded yet.')).toBeInTheDocument()
    expect(screen.getByText('No events recorded yet.')).toBeInTheDocument()
  })

  it('explains when details are hidden, rather than saying there are none', async () => {
    signedInWithRoles('Family.Viewer')
    profileIs({ ...profile, isPrivate: true, events: [] })
    renderApp(signedIn, '/family/morag')

    expect(await screen.findByText(/hidden, because this person may be living/)).toBeInTheDocument()
    expect(screen.queryByText('No events recorded yet.')).not.toBeInTheDocument()
    expect(screen.getByRole('link', { name: 'Anna MacLeod' })).toBeInTheDocument()
  })

  it('offers viewers no way to edit', async () => {
    await openProfile('Family.Viewer')

    await screen.findByRole('link', { name: 'Search' })
    expect(screen.queryByRole('link', { name: 'Edit' })).not.toBeInTheDocument()
  })

  it('links admins to the editing screens', async () => {
    await openProfile('Family.Admin')

    expect(await screen.findByRole('link', { name: 'Edit' })).toHaveAttribute('href', '/people/morag')
  })

  it('says so when the person does not exist', async () => {
    signedInWithRoles('Family.Viewer')
    server.use(http.get('*/api/v1/people/missing', () => new HttpResponse(null, { status: 404 })))

    renderApp(signedIn, '/family/missing')

    expect(await screen.findByRole('alert')).toHaveTextContent("This person doesn't exist.")
  })

  it('says so when the profile cannot be loaded', async () => {
    signedInWithRoles('Family.Viewer')
    server.use(http.get('*/api/v1/people/morag', () => new HttpResponse(null, { status: 500 })))

    renderApp(signedIn, '/family/morag')

    expect(await screen.findByRole('alert')).toHaveTextContent("This person couldn't be loaded.")
  })
})
