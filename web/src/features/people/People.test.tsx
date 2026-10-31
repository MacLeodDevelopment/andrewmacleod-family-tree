import { fireEvent, screen, waitFor, within } from '@testing-library/react'
import { http, HttpResponse } from 'msw'
import { describe, expect, it } from 'vitest'
import { fakeAuth, renderApp, testAccessToken } from '../../test/renderApp.tsx'
import { server } from '../../test/server.ts'
import type { Person } from './people.ts'

const signedIn = fakeAuth({ user: { name: 'Andrew', username: 'andrew@example.test' } })
const problemJson = { headers: { 'Content-Type': 'application/problem+json' } }

const morag: Person = {
  id: 'morag-id',
  sex: 'Female',
  names: [{ givenName: 'Morag', surname: 'MacLeod', type: 'Birth' }],
  events: [{ id: 'e1', type: 'Birth', date: 'c. 1870', dateAsEntered: 'abt 1870', place: 'Innellan' }],
}

function signedInWithRoles(...roles: string[]) {
  server.use(http.get('*/api/v1/me', () => HttpResponse.json({ name: 'Andrew', username: 'a@example.test', roles })))
}

function peopleAre(...people: Person[]) {
  server.use(http.get('*/api/v1/admin/people', () => HttpResponse.json({ items: people, total: people.length })))
}

function personIs(person: Person) {
  server.use(http.get(`*/api/v1/admin/people/${person.id}`, () => HttpResponse.json(person)))
}

const type = (label: string, value: string) => fireEvent.change(screen.getByLabelText(label), { target: { value } })
const click = (name: string) => fireEvent.click(screen.getByRole('button', { name }))

describe('Who can see the people screens', () => {
  it('asks visitors to sign in', () => {
    renderApp(fakeAuth(), '/people')

    expect(screen.getByText('Please sign in to see this page.')).toBeInTheDocument()
  })

  it('tells viewers they have no access, and offers them no link', async () => {
    signedInWithRoles('Family.Viewer')

    renderApp(signedIn, '/people')

    expect(await screen.findByRole('alert')).toHaveTextContent("You don't have access to this page.")
    expect(screen.queryByRole('link', { name: 'People' })).not.toBeInTheDocument()
  })

  it("doesn't blame the user when the API can't be reached", async () => {
    server.use(http.get('*/api/v1/me', () => new HttpResponse(null, { status: 503 })))

    renderApp(signedIn, '/people')

    expect(await screen.findByRole('alert')).toHaveTextContent("The family tree couldn't be reached.")
  })

  it('gives admins a link to the people screens', async () => {
    signedInWithRoles('Family.Admin')

    renderApp(signedIn)

    expect(await screen.findByRole('link', { name: 'People' })).toHaveAttribute('href', '/people')
  })
})

describe('The list of people', () => {
  it('shows each person with their dates, and how many there are', async () => {
    signedInWithRoles('Family.Admin')
    peopleAre(morag)

    renderApp(signedIn, '/people')

    expect(await screen.findByRole('link', { name: 'Morag MacLeod' })).toHaveAttribute('href', '/people/morag-id')
    expect(screen.getByText('b. c. 1870')).toBeInTheDocument()
    expect(screen.getByText('1 person')).toBeInTheDocument()
  })

  it('includes someone straight after they are added', async () => {
    signedInWithRoles('Family.Admin')
    let people: Person[] = []
    server.use(
      http.get('*/api/v1/admin/people', () => HttpResponse.json({ items: people, total: people.length })),
      http.post('*/api/v1/admin/people', () => {
        people = [morag]
        return HttpResponse.json(morag, { status: 201 })
      }),
    )
    personIs(morag)
    renderApp(signedIn, '/people')
    await screen.findByText('Nobody has been added yet.')

    fireEvent.click(screen.getByRole('link', { name: 'Add a person' }))
    type('Given name', 'Morag')
    click('Save')
    fireEvent.click(await screen.findByRole('link', { name: 'Back to people' }))

    expect(await screen.findByRole('link', { name: 'Morag MacLeod' })).toBeInTheDocument()
  })

  it('says so when nobody has been added', async () => {
    signedInWithRoles('Family.Admin')
    peopleAre()

    renderApp(signedIn, '/people')

    expect(await screen.findByText('Nobody has been added yet.')).toBeInTheDocument()
  })

  it('moves through long lists a page at a time', async () => {
    signedInWithRoles('Family.Admin')
    server.use(
      http.get('*/api/v1/admin/people', ({ request }) => {
        const skip = new URL(request.url).searchParams.get('skip')
        const person = { ...morag, id: `person-${skip}`, names: [{ givenName: `Page from ${skip}`, surname: null, type: 'Birth' as const }] }
        return HttpResponse.json({ items: [person], total: 60 })
      }),
    )
    renderApp(signedIn, '/people')
    await screen.findByRole('link', { name: 'Page from 0' })

    click('Next')

    expect(await screen.findByRole('link', { name: 'Page from 50' })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Next' })).toBeDisabled()
  })
})

describe('Adding a person', () => {
  it('saves them and shows their page', async () => {
    signedInWithRoles('Family.Admin')
    let sent: { token: string | null; body: unknown } | undefined
    server.use(
      http.post('*/api/v1/admin/people', async ({ request }) => {
        sent = { token: request.headers.get('Authorization'), body: await request.json() }
        return HttpResponse.json(morag, { status: 201 })
      }),
    )
    personIs(morag)
    renderApp(signedIn, '/people/new')
    await screen.findByRole('heading', { name: 'Add a person' })

    type('Given name', 'Morag')
    type('Surname', 'MacLeod')
    type('Sex', 'Female')
    click('Save')

    expect(await screen.findByRole('heading', { level: 1, name: 'Morag MacLeod' })).toBeInTheDocument()
    expect(sent).toEqual({
      token: `Bearer ${testAccessToken}`,
      body: { sex: 'Female', names: [{ givenName: 'Morag', surname: 'MacLeod', type: 'Birth' }] },
    })
  })

  it('can record more than one name', async () => {
    signedInWithRoles('Family.Admin')
    let names: unknown
    server.use(
      http.post('*/api/v1/admin/people', async ({ request }) => {
        names = ((await request.json()) as { names: unknown }).names
        return HttpResponse.json(morag, { status: 201 })
      }),
    )
    personIs(morag)
    renderApp(signedIn, '/people/new')
    await screen.findByRole('heading', { name: 'Add a person' })

    click('Add another name')
    const [main, other] = screen.getAllByRole('group')
    fireEvent.change(within(main).getByLabelText('Given name'), { target: { value: 'Isla' } })
    fireEvent.change(within(main).getByLabelText('Surname'), { target: { value: 'Campbell' } })
    fireEvent.change(within(other).getByLabelText('Given name'), { target: { value: 'Isla' } })
    fireEvent.change(within(other).getByLabelText('Surname'), { target: { value: 'MacLeod' } })
    click('Save')

    await waitFor(() =>
      expect(names).toEqual([
        { givenName: 'Isla', surname: 'Campbell', type: 'Birth' },
        { givenName: 'Isla', surname: 'MacLeod', type: 'Married' },
      ]),
    )
  })

  it('shows the reason next to the field when something is wrong', async () => {
    signedInWithRoles('Family.Admin')
    server.use(
      http.post('*/api/v1/admin/people', () =>
        HttpResponse.json({ errors: { 'names[0].givenName': ['A given name is required.'] } }, { status: 400, ...problemJson }),
      ),
    )
    renderApp(signedIn, '/people/new')
    await screen.findByRole('heading', { name: 'Add a person' })

    click('Save')

    expect(await screen.findByRole('alert')).toHaveTextContent('A given name is required.')
    expect(screen.getByLabelText('Given name')).toHaveAttribute('aria-invalid', 'true')
  })

  it('stops at a possible duplicate, shows the match, and can add anyway', async () => {
    signedInWithRoles('Family.Admin')
    const requests: string[] = []
    server.use(
      http.post('*/api/v1/admin/people', ({ request }) => {
        requests.push(new URL(request.url).search)
        return new URL(request.url).search
          ? HttpResponse.json(morag, { status: 201 })
          : HttpResponse.json({ title: 'This may be a duplicate', duplicates: ['match-id'] }, { status: 409, ...problemJson })
      }),
    )
    personIs(morag)
    renderApp(signedIn, '/people/new')
    await screen.findByRole('heading', { name: 'Add a person' })

    type('Given name', 'Morag')
    click('Save')

    expect(await screen.findByRole('link', { name: 'See match 1' })).toHaveAttribute('href', '/people/match-id')
    click("It's a different person: add anyway")

    expect(await screen.findByRole('heading', { level: 1, name: 'Morag MacLeod' })).toBeInTheDocument()
    expect(requests).toEqual(['', '?allowDuplicate=true'])
  })

  it('says so when saving fails for another reason', async () => {
    signedInWithRoles('Family.Admin')
    server.use(http.post('*/api/v1/admin/people', () => new HttpResponse(null, { status: 500 })))
    renderApp(signedIn, '/people/new')
    await screen.findByRole('heading', { name: 'Add a person' })

    click('Save')

    expect(await screen.findByRole('alert')).toHaveTextContent("This person couldn't be saved.")
  })
})

describe("A person's page", () => {
  it('shows their life events', async () => {
    signedInWithRoles('Family.Admin')
    personIs(morag)

    renderApp(signedIn, '/people/morag-id')

    expect(await screen.findByRole('heading', { level: 1, name: 'Morag MacLeod' })).toBeInTheDocument()
    expect(screen.getByRole('listitem')).toHaveTextContent('Birth · c. 1870 · Innellan')
  })

  it('says so when the person does not exist', async () => {
    signedInWithRoles('Family.Admin')
    server.use(http.get('*/api/v1/admin/people/missing', () => new HttpResponse(null, { status: 404 })))

    renderApp(signedIn, '/people/missing')

    expect(await screen.findByRole('alert')).toHaveTextContent("This person doesn't exist.")
  })

  it('records an event and shows it', async () => {
    signedInWithRoles('Family.Admin')
    let current: Person = { ...morag, events: [] }
    let sent: unknown
    server.use(
      http.get('*/api/v1/admin/people/morag-id', () => HttpResponse.json(current)),
      http.post('*/api/v1/admin/people/morag-id/events', async ({ request }) => {
        sent = await request.json()
        current = morag
        return HttpResponse.json(morag, { status: 201 })
      }),
    )
    renderApp(signedIn, '/people/morag-id')
    await screen.findByText('No events recorded yet.')

    type('Date', 'abt 1870')
    type('Place', 'Innellan')
    click('Add event')

    expect(await screen.findByText(/c\. 1870/)).toBeInTheDocument()
    expect(sent).toEqual({ type: 'Birth', date: 'abt 1870', place: 'Innellan' })
  })

  it('only offers events the person can still have', async () => {
    signedInWithRoles('Family.Admin')
    personIs(morag)

    renderApp(signedIn, '/people/morag-id')

    const event = await screen.findByLabelText('Event')
    expect(within(event).getAllByRole('option').map((option) => option.textContent)).toEqual(['Death'])
  })

  it('explains a date it cannot understand', async () => {
    signedInWithRoles('Family.Admin')
    personIs({ ...morag, events: [] })
    server.use(
      http.post('*/api/v1/admin/people/morag-id/events', () =>
        HttpResponse.json({ errors: { date: ['"31 Feb 1900" isn\'t a date this understands.'] } }, { status: 400, ...problemJson }),
      ),
    )
    renderApp(signedIn, '/people/morag-id')
    await screen.findByText('No events recorded yet.')

    type('Date', '31 Feb 1900')
    click('Add event')

    expect(await screen.findByRole('alert')).toHaveTextContent("isn't a date this understands")
    expect(screen.getByLabelText('Date')).toHaveValue('31 Feb 1900')
  })

  it('deletes someone only after confirming, then returns to the list', async () => {
    signedInWithRoles('Family.Admin')
    let deleted = false
    personIs(morag)
    peopleAre()
    server.use(
      http.delete('*/api/v1/admin/people/morag-id', () => {
        deleted = true
        return new HttpResponse(null, { status: 204 })
      }),
    )
    renderApp(signedIn, '/people/morag-id')
    await screen.findByRole('heading', { level: 1, name: 'Morag MacLeod' })

    click('Delete this person')
    expect(deleted).toBe(false)
    click('Yes, delete')

    expect(await screen.findByRole('heading', { level: 1, name: 'People' })).toBeInTheDocument()
    expect(deleted).toBe(true)
  })

  it('can back out of deleting', async () => {
    signedInWithRoles('Family.Admin')
    personIs(morag)
    renderApp(signedIn, '/people/morag-id')
    await screen.findByRole('heading', { level: 1, name: 'Morag MacLeod' })

    click('Delete this person')
    click('Cancel')

    expect(screen.queryByRole('button', { name: 'Yes, delete' })).not.toBeInTheDocument()
  })

  it('explains why someone in a family cannot be deleted', async () => {
    signedInWithRoles('Family.Admin')
    personIs(morag)
    server.use(
      http.delete('*/api/v1/admin/people/morag-id', () =>
        HttpResponse.json({ title: 'This person is part of a family' }, { status: 409, ...problemJson }),
      ),
    )
    renderApp(signedIn, '/people/morag-id')
    await screen.findByRole('heading', { level: 1, name: 'Morag MacLeod' })

    click('Delete this person')
    click('Yes, delete')

    expect(await screen.findByRole('alert')).toHaveTextContent('Remove them from their families first.')
  })
})
