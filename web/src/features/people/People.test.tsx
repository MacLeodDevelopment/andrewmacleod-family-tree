import { fireEvent, screen, waitFor, within } from '@testing-library/react'
import { http, HttpResponse } from 'msw'
import { describe, expect, it } from 'vitest'
import { fakeAuth, renderApp, testAccessToken } from '../../test/renderApp.tsx'
import { server } from '../../test/server.ts'
import type { Family, PersonFamilies } from './families.ts'
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

function savesEventChanges(onChange: (body: unknown) => Person) {
  server.use(
    http.put('*/api/v1/admin/people/morag-id/events/e1', async ({ request }) => {
      const changed = onChange(await request.json())
      personIs(changed)
      return HttpResponse.json(changed)
    }),
  )
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
          : HttpResponse.json({ title: 'This may be a duplicate', matches: [{ ...morag, id: 'match-id' }] }, { status: 409, ...problemJson })
      }),
    )
    personIs(morag)
    renderApp(signedIn, '/people/new')
    await screen.findByRole('heading', { name: 'Add a person' })

    type('Given name', 'Morag')
    click('Save')

    const match = await screen.findByRole('link', { name: 'Morag MacLeod' })
    expect(match).toHaveAttribute('href', '/people/match-id')
    expect(match.closest('li')).toHaveTextContent('b. c. 1870')
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

  it('corrects an event, starting from the date as it was typed', async () => {
    signedInWithRoles('Family.Admin')
    personIs(morag)
    let sent: unknown
    savesEventChanges((body) => {
      sent = body
      return { ...morag, events: [{ ...morag.events[0], date: '1871', place: 'Dunoon' }] }
    })
    renderApp(signedIn, '/people/morag-id')
    await screen.findByRole('heading', { level: 1, name: 'Morag MacLeod' })

    click('Edit birth')
    const form = within(screen.getByRole('form', { name: 'Edit birth' }))
    expect(form.getByLabelText('Date')).toHaveValue('abt 1870')
    fireEvent.change(form.getByLabelText('Date'), { target: { value: '1871' } })
    fireEvent.change(form.getByLabelText('Place'), { target: { value: 'Dunoon' } })
    fireEvent.click(form.getByRole('button', { name: 'Save' }))

    expect(await screen.findByText(/1871 · Dunoon/)).toBeInTheDocument()
    expect(screen.queryByRole('form', { name: 'Edit birth' })).not.toBeInTheDocument()
    expect(sent).toEqual({ date: '1871', place: 'Dunoon' })
  })

  it('keeps an event open with the reason when a correction is refused', async () => {
    signedInWithRoles('Family.Admin')
    personIs(morag)
    server.use(
      http.put('*/api/v1/admin/people/morag-id/events/e1', () =>
        HttpResponse.json({ errors: { date: ["That isn't a date this understands."] } }, { status: 400, ...problemJson }),
      ),
    )
    renderApp(signedIn, '/people/morag-id')
    await screen.findByRole('heading', { level: 1, name: 'Morag MacLeod' })

    click('Edit birth')
    const form = within(screen.getByRole('form', { name: 'Edit birth' }))
    fireEvent.change(form.getByLabelText('Date'), { target: { value: '31 Feb 1900' } })
    fireEvent.click(form.getByRole('button', { name: 'Save' }))

    expect(await form.findByRole('alert')).toHaveTextContent("isn't a date this understands")
    expect(form.getByLabelText('Date')).toHaveValue('31 Feb 1900')
  })

  it('can back out of correcting an event', async () => {
    signedInWithRoles('Family.Admin')
    personIs(morag)
    renderApp(signedIn, '/people/morag-id')
    await screen.findByRole('heading', { level: 1, name: 'Morag MacLeod' })

    click('Edit birth')
    click('Cancel')

    expect(screen.getByText(/c\. 1870/)).toBeInTheDocument()
    expect(screen.queryByRole('form', { name: 'Edit birth' })).not.toBeInTheDocument()
  })

  it('removes an event only after confirming, and offers it again', async () => {
    signedInWithRoles('Family.Admin')
    personIs(morag)
    let removed = false
    server.use(
      http.delete('*/api/v1/admin/people/morag-id/events/e1', () => {
        removed = true
        personIs({ ...morag, events: [] })
        return new HttpResponse(null, { status: 204 })
      }),
    )
    renderApp(signedIn, '/people/morag-id')
    await screen.findByRole('heading', { level: 1, name: 'Morag MacLeod' })

    click('Remove birth')
    expect(removed).toBe(false)
    click('Yes, remove')

    expect(await screen.findByText('No events recorded yet.')).toBeInTheDocument()
    expect(within(screen.getByLabelText('Event')).getAllByRole('option').map((option) => option.textContent)).toEqual(['Birth', 'Death'])
  })

  it('says so when an event cannot be removed', async () => {
    signedInWithRoles('Family.Admin')
    personIs(morag)
    server.use(http.delete('*/api/v1/admin/people/morag-id/events/e1', () => new HttpResponse(null, { status: 500 })))
    renderApp(signedIn, '/people/morag-id')
    await screen.findByRole('heading', { level: 1, name: 'Morag MacLeod' })

    click('Remove birth')
    click('Yes, remove')

    expect(await screen.findByRole('alert')).toHaveTextContent("This event couldn't be removed.")
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

describe('Editing a person', () => {
  const isla: Person = {
    id: 'isla-id',
    sex: 'Unknown',
    names: [
      { givenName: 'Isla', surname: 'Campbell', type: 'Birth' },
      { givenName: 'Isla', surname: null, type: 'Nickname' },
    ],
    events: [],
  }

  async function openEditor() {
    signedInWithRoles('Family.Admin')
    personIs(isla)
    renderApp(signedIn, '/people/isla-id')
    fireEvent.click(await screen.findByRole('link', { name: 'Edit' }))
    await screen.findByRole('heading', { name: 'Edit Isla Campbell' })
  }

  it('starts from what is recorded, saves changes and shows the person', async () => {
    let sent: unknown
    server.use(
      http.put('*/api/v1/admin/people/isla-id', async ({ request }) => {
        sent = await request.json()
        const changed = { ...isla, names: [{ ...isla.names[0], surname: 'MacLeod' }] }
        personIs(changed)
        return HttpResponse.json(changed)
      }),
    )
    await openEditor()
    const [main, other] = screen.getAllByRole('group')
    expect(within(main).getByLabelText('Surname')).toHaveValue('Campbell')
    expect(within(other).getByLabelText('Type of name')).toHaveValue('Nickname')

    fireEvent.change(within(main).getByLabelText('Surname'), { target: { value: 'MacLeod' } })
    type('Sex', 'Female')
    click('Remove this name')
    click('Save')

    expect(await screen.findByRole('heading', { level: 1, name: 'Isla MacLeod' })).toBeInTheDocument()
    expect(sent).toEqual({ sex: 'Female', names: [{ givenName: 'Isla', surname: 'MacLeod', type: 'Birth' }] })
  })

  it('updates the list too', async () => {
    let people = [isla]
    server.use(
      http.get('*/api/v1/admin/people', () => HttpResponse.json({ items: people, total: people.length })),
      http.put('*/api/v1/admin/people/isla-id', () => {
        const changed = { ...isla, names: [{ ...isla.names[0], surname: 'MacLeod' }] }
        people = [changed]
        personIs(changed)
        return HttpResponse.json(changed)
      }),
    )
    await openEditor()

    click('Save')
    fireEvent.click(await screen.findByRole('link', { name: 'Back to people' }))

    expect(await screen.findByRole('link', { name: 'Isla MacLeod' })).toBeInTheDocument()
  })

  it('shows the reason next to the field, keeping what was typed', async () => {
    server.use(
      http.put('*/api/v1/admin/people/isla-id', () =>
        HttpResponse.json({ errors: { 'names[1].givenName': ['A given name is required.'] } }, { status: 400, ...problemJson }),
      ),
    )
    await openEditor()
    const other = screen.getAllByRole('group')[1]

    fireEvent.change(within(other).getByLabelText('Given name'), { target: { value: '' } })
    click('Save')

    expect(await within(other).findByRole('alert')).toHaveTextContent('A given name is required.')
    expect(within(other).getByLabelText('Given name')).toHaveAttribute('aria-invalid', 'true')
  })

  it('says so when saving fails for another reason', async () => {
    server.use(http.put('*/api/v1/admin/people/isla-id', () => new HttpResponse(null, { status: 500 })))
    await openEditor()

    click('Save')

    expect(await screen.findByRole('alert')).toHaveTextContent("The changes couldn't be saved.")
  })

  it('goes back to the person without saving when cancelled', async () => {
    await openEditor()

    fireEvent.click(screen.getByRole('link', { name: 'Cancel' }))

    expect(await screen.findByRole('heading', { level: 1, name: 'Isla Campbell' })).toBeInTheDocument()
  })

  it('is closed to viewers', async () => {
    signedInWithRoles('Family.Viewer')

    renderApp(signedIn, '/people/isla-id/edit')

    expect(await screen.findByRole('alert')).toHaveTextContent("You don't have access to this page.")
  })
})

describe("A person's family", () => {
  const person = (id: string, givenName: string, surname = 'MacLeod'): Person => ({
    id,
    sex: 'Unknown',
    names: [{ givenName, surname, type: 'Birth' }],
    events: [],
  })
  const [ewan, ishbel, anna, calum, flora, rory] = [
    person('ewan', 'Ewan'),
    person('ishbel', 'Ishbel', 'Campbell'),
    person('anna', 'Anna'),
    person('calum', 'Calum'),
    person('flora', 'Flora'),
    person('rory', 'Rory'),
  ]

  function familiesAre(families: Partial<PersonFamilies>) {
    server.use(
      http.get('*/api/v1/admin/people/ewan/families', () =>
        HttpResponse.json({ asChild: [], asPartner: [], people: [], ...families }),
      ),
    )
  }

  function searchFinds(...found: Person[]) {
    server.use(http.get('*/api/v1/admin/people', () => HttpResponse.json({ items: found, total: found.length })))
  }

  async function openEwan() {
    signedInWithRoles('Family.Admin')
    personIs(ewan)
    renderApp(signedIn, '/people/ewan')
    await screen.findByRole('heading', { name: 'Family' })
  }

  async function choose(name: RegExp) {
    type('Find someone', 'someone')
    fireEvent.click(await screen.findByRole('radio', { name }))
  }

  it('shows parents, brothers and sisters, partners and children, linking to each', async () => {
    const birthFamily: Family = {
      id: 'birth',
      partner1Id: 'anna',
      partner2Id: null,
      type: 'None',
      children: [
        { childId: 'ewan', partner1Relationship: 'Adopted', partner2Relationship: 'Unknown' },
        { childId: 'flora', partner1Relationship: 'Biological', partner2Relationship: 'Unknown' },
      ],
    }
    const marriage: Family = {
      id: 'marriage',
      partner1Id: 'ishbel',
      partner2Id: 'ewan',
      type: 'Marriage',
      children: [{ childId: 'calum', partner1Relationship: 'Biological', partner2Relationship: 'Step' }],
    }
    familiesAre({ asChild: [birthFamily], asPartner: [marriage], people: [ewan, ishbel, anna, calum, flora] })

    await openEwan()

    expect(await screen.findByRole('link', { name: 'Anna MacLeod' })).toHaveAttribute('href', '/people/anna')
    expect(screen.getByRole('link', { name: 'Anna MacLeod' }).closest('li')).toHaveTextContent('(adopted)')
    expect(within(screen.getByRole('link', { name: 'Anna MacLeod' }).closest('ul')!).getByText('Unknown')).toBeInTheDocument()
    expect(screen.getByRole('link', { name: 'Flora MacLeod' })).toBeInTheDocument()
    expect(screen.queryByRole('link', { name: 'Ewan MacLeod' })).not.toBeInTheDocument()
    expect(screen.getByRole('heading', { level: 3, name: /With Ishbel Campbell · Marriage/ })).toBeInTheDocument()
    expect(screen.getByRole('link', { name: 'Calum MacLeod' }).closest('li')).toHaveTextContent('(step)')
  })

  it('says so when no family is recorded', async () => {
    await openEwan()

    expect(screen.getByText('No family recorded yet.')).toBeInTheDocument()
  })

  it('links an existing person as a partner and shows them', async () => {
    let sent: unknown
    await openEwan()
    searchFinds(ewan, ishbel)
    server.use(
      http.post('*/api/v1/admin/families', async ({ request }) => {
        sent = await request.json()
        familiesAre({
          asPartner: [{ id: 'f', partner1Id: 'ewan', partner2Id: 'ishbel', type: 'Cohabiting', children: [] }],
          people: [ewan, ishbel],
        })
        return HttpResponse.json({}, { status: 201 })
      }),
    )

    click('Add a partner')
    await choose(/Ishbel Campbell/)
    expect(screen.queryByRole('radio', { name: /Ewan/ })).not.toBeInTheDocument()
    type('Type of partnership', 'Cohabiting')
    click('Save')

    expect(await screen.findByRole('heading', { level: 3, name: /With Ishbel Campbell · Living together/ })).toBeInTheDocument()
    expect(sent).toEqual({ partner1Id: 'ewan', partner2Id: 'ishbel', type: 'Cohabiting' })
  })

  it('searches by the name typed', async () => {
    let search: string | null = null
    await openEwan()
    server.use(
      http.get('*/api/v1/admin/people', ({ request }) => {
        search = new URL(request.url).searchParams.get('search')
        return HttpResponse.json({ items: [], total: 0 })
      }),
    )

    click('Add a partner')
    type('Find someone', 'Ishbel Mac')

    expect(await screen.findByText(/Nobody found/)).toBeInTheDocument()
    expect(search).toBe('Ishbel Mac')
  })

  it("won't save until someone is chosen", async () => {
    await openEwan()

    click('Add a child')

    expect(screen.getByRole('button', { name: 'Save' })).toBeDisabled()
  })

  it('adds a child to a partnership, with a relationship to each parent', async () => {
    let sent: unknown
    familiesAre({
      asPartner: [{ id: 'marriage', partner1Id: 'ishbel', partner2Id: 'ewan', type: 'Marriage', children: [] }],
      people: [ewan, ishbel],
    })
    await openEwan()
    searchFinds(rory)
    server.use(
      http.post('*/api/v1/admin/families/marriage/children', async ({ request }) => {
        sent = await request.json()
        familiesAre({
          asPartner: [
            {
              id: 'marriage',
              partner1Id: 'ishbel',
              partner2Id: 'ewan',
              type: 'Marriage',
              children: [{ childId: 'rory', partner1Relationship: 'Biological', partner2Relationship: 'Step' }],
            },
          ],
          people: [ewan, ishbel, rory],
        })
        return HttpResponse.json({}, { status: 201 })
      }),
    )

    click('Add a child')
    await choose(/Rory/)
    type('Relationship to Ewan MacLeod', 'Step')
    click('Save')

    expect(await screen.findByRole('link', { name: 'Rory MacLeod' })).toBeInTheDocument()
    expect(sent).toEqual({ childId: 'rory', partner1Relationship: 'Biological', partner2Relationship: 'Step' })
  })

  it('records a child whose other parent is unknown in one step', async () => {
    let sent: unknown
    await openEwan()
    searchFinds(rory)
    server.use(
      http.post('*/api/v1/admin/families', async ({ request }) => {
        sent = await request.json()
        return HttpResponse.json({}, { status: 201 })
      }),
    )

    click('Add a child')
    await choose(/Rory/)
    expect(screen.queryByLabelText(/Relationship to Unknown/)).not.toBeInTheDocument()
    click('Save')

    await waitFor(() =>
      expect(sent).toEqual({
        partner1Id: 'ewan',
        type: 'None',
        children: [{ childId: 'rory', partner1Relationship: 'Biological' }],
      }),
    )
  })

  it('reuses a family with an unknown other parent', async () => {
    familiesAre({
      asPartner: [{ id: 'alone', partner1Id: 'ewan', partner2Id: null, type: 'None', children: [] }],
      people: [ewan],
    })
    await openEwan()

    click('Add a child')

    const options = within(screen.getByLabelText('Other parent')).getAllByRole('option')
    expect(options.map((option) => option.textContent)).toEqual(['Unknown'])
    expect(screen.getByLabelText('Other parent')).toHaveValue('alone')
  })

  it("shows the API's reason when a link is refused", async () => {
    await openEwan()
    searchFinds(rory)
    server.use(
      http.post('*/api/v1/admin/families', () =>
        HttpResponse.json({ errors: { childId: ['This would make someone their own ancestor.'] } }, { status: 400, ...problemJson }),
      ),
    )

    click('Add a child')
    await choose(/Rory/)
    click('Save')

    expect(await screen.findByRole('alert')).toHaveTextContent('This would make someone their own ancestor.')
  })

  const anna_alone: Family = {
    id: 'birth',
    partner1Id: 'anna',
    partner2Id: null,
    type: 'None',
    children: [{ childId: 'ewan', partner1Relationship: 'Biological', partner2Relationship: 'Unknown' }],
  }

  it('names an unknown parent, alongside the parent already known', async () => {
    let sent: unknown
    familiesAre({ asChild: [anna_alone], people: [ewan, anna] })
    await openEwan()
    searchFinds(rory)
    server.use(
      http.post('*/api/v1/admin/families/birth/partners', async ({ request }) => {
        sent = await request.json()
        familiesAre({ asChild: [{ ...anna_alone, partner2Id: 'rory' }], people: [ewan, anna, rory] })
        return HttpResponse.json({})
      }),
    )

    click('Add a parent')
    await choose(/Rory/)
    expect(screen.getByLabelText('Alongside')).toHaveValue('birth')
    type('Relationship to Ewan MacLeod', 'Presumed')
    click('Save')

    expect(await screen.findByRole('link', { name: 'Rory MacLeod' })).toBeInTheDocument()
    expect(sent).toEqual({ partnerId: 'rory', relationship: 'Presumed' })
  })

  it('warns when a new parent would also become the parent of brothers and sisters', async () => {
    familiesAre({
      asChild: [
        {
          ...anna_alone,
          children: [...anna_alone.children, { childId: 'flora', partner1Relationship: 'Biological', partner2Relationship: 'Unknown' }],
        },
      ],
      people: [ewan, anna, flora],
    })
    await openEwan()

    click('Add a parent')

    expect(screen.getByText(/parent of everyone in this family/)).toBeInTheDocument()
  })

  it('records a second set of parents as a new family', async () => {
    let sent: unknown
    familiesAre({ asChild: [{ ...anna_alone, partner2Id: 'flora' }], people: [ewan, anna, flora] })
    await openEwan()
    searchFinds(rory)
    server.use(
      http.post('*/api/v1/admin/families', async ({ request }) => {
        sent = await request.json()
        return HttpResponse.json({}, { status: 201 })
      }),
    )

    click('Add a parent')
    await choose(/Rory/)
    expect(within(screen.getByLabelText('Alongside')).getAllByRole('option')).toHaveLength(1)
    type('Relationship to Ewan MacLeod', 'Adopted')
    click('Save')

    await waitFor(() =>
      expect(sent).toEqual({
        partner1Id: 'rory',
        type: 'Unknown',
        children: [{ childId: 'ewan', partner1Relationship: 'Adopted' }],
      }),
    )
  })

  it('adds a brother or sister to the family chosen', async () => {
    let sent: unknown
    familiesAre({ asChild: [anna_alone], people: [ewan, anna] })
    await openEwan()
    searchFinds(flora)
    server.use(
      http.post('*/api/v1/admin/families/birth/children', async ({ request }) => {
        sent = await request.json()
        return HttpResponse.json({}, { status: 201 })
      }),
    )

    click('Add a brother or sister')
    await choose(/Flora/)
    click('Save')

    await waitFor(() =>
      expect(sent).toEqual({ childId: 'flora', partner1Relationship: 'Biological', partner2Relationship: 'Biological' }),
    )
  })

  it('only offers a brother or sister once there are parents to share', async () => {
    await openEwan()

    expect(screen.queryByRole('button', { name: 'Add a brother or sister' })).not.toBeInTheDocument()
  })

  it('corrects the type of a partnership', async () => {
    let sent: unknown
    const marriage: Family = { id: 'f', partner1Id: 'ewan', partner2Id: 'ishbel', type: 'Unknown', children: [] }
    familiesAre({ asPartner: [marriage], people: [ewan, ishbel] })
    await openEwan()
    server.use(
      http.put('*/api/v1/admin/families/f', async ({ request }) => {
        sent = await request.json()
        familiesAre({ asPartner: [{ ...marriage, type: 'Marriage' }], people: [ewan, ishbel] })
        return HttpResponse.json({})
      }),
    )

    click('Change partnership with Ishbel Campbell')
    type('Type of partnership', 'Marriage')
    click('Save')

    expect(await screen.findByRole('heading', { level: 3, name: /· Marriage/ })).toBeInTheDocument()
    await waitFor(() => expect(screen.queryByRole('form', { name: /Change partnership/ })).not.toBeInTheDocument())
    expect(sent).toEqual({ type: 'Marriage' })
  })

  it('undoes a partnership only after confirming, keeping the children with this person', async () => {
    let removed = false
    const family: Family = {
      id: 'f',
      partner1Id: 'ewan',
      partner2Id: 'ishbel',
      type: 'Marriage',
      children: [{ childId: 'calum', partner1Relationship: 'Biological', partner2Relationship: 'Biological' }],
    }
    familiesAre({ asPartner: [family], people: [ewan, ishbel, calum] })
    await openEwan()
    server.use(
      http.delete('*/api/v1/admin/families/f/partners/ishbel', () => {
        removed = true
        familiesAre({ asPartner: [{ ...family, partner2Id: null }], people: [ewan, calum] })
        return new HttpResponse(null, { status: 204 })
      }),
    )

    click('Remove partnership with Ishbel Campbell')
    expect(screen.getByText(/The children stay with Ewan MacLeod/)).toBeInTheDocument()
    expect(removed).toBe(false)
    click('Yes, remove')

    expect(await screen.findByRole('heading', { level: 3, name: /With someone unknown/ })).toBeInTheDocument()
    expect(removed).toBe(true)
  })

  it('takes a child out of a family', async () => {
    const family: Family = {
      id: 'f',
      partner1Id: 'ewan',
      partner2Id: null,
      type: 'None',
      children: [{ childId: 'calum', partner1Relationship: 'Biological', partner2Relationship: 'Unknown' }],
    }
    familiesAre({ asPartner: [family], people: [ewan, calum] })
    await openEwan()
    server.use(
      http.delete('*/api/v1/admin/families/f/children/calum', () => {
        familiesAre({ asPartner: [{ ...family, children: [] }], people: [ewan] })
        return new HttpResponse(null, { status: 204 })
      }),
    )

    click('Remove Calum MacLeod from this family')
    click('Yes, remove')

    await waitFor(() => expect(screen.queryByRole('link', { name: 'Calum MacLeod' })).not.toBeInTheDocument())
  })

  it('takes this person out of the family they are a child of', async () => {
    familiesAre({ asChild: [anna_alone], people: [ewan, anna] })
    await openEwan()
    server.use(
      http.delete('*/api/v1/admin/families/birth/children/ewan', () => {
        familiesAre({})
        return new HttpResponse(null, { status: 204 })
      }),
    )

    click('Remove from these parents: Anna MacLeod')
    click('Yes, remove')

    expect(await screen.findByText('No family recorded yet.')).toBeInTheDocument()
  })

  it('removes an empty family with an unknown partner', async () => {
    let removed = false
    familiesAre({
      asPartner: [{ id: 'alone', partner1Id: 'ewan', partner2Id: null, type: 'None', children: [] }],
      people: [ewan],
    })
    await openEwan()
    server.use(
      http.delete('*/api/v1/admin/families/alone', () => {
        removed = true
        familiesAre({})
        return new HttpResponse(null, { status: 204 })
      }),
    )

    click('Remove partnership with someone unknown')
    click('Yes, remove')

    expect(await screen.findByText('No family recorded yet.')).toBeInTheDocument()
    expect(removed).toBe(true)
  })

  it("shows the API's reason when a link can't be undone", async () => {
    const family: Family = { id: 'f', partner1Id: 'ewan', partner2Id: 'ishbel', type: 'Marriage', children: [] }
    familiesAre({ asPartner: [family], people: [ewan, ishbel] })
    await openEwan()
    server.use(
      http.delete('*/api/v1/admin/families/f/partners/ishbel', () =>
        HttpResponse.json({ errors: { partnerId: ['A family needs at least one known partner.'] } }, { status: 400, ...problemJson }),
      ),
    )

    click('Remove partnership with Ishbel Campbell')
    click('Yes, remove')

    expect(await screen.findByRole('alert')).toHaveTextContent('A family needs at least one known partner.')
  })

  it('can back out of undoing a link', async () => {
    const family: Family = { id: 'f', partner1Id: 'ewan', partner2Id: 'ishbel', type: 'Marriage', children: [] }
    familiesAre({ asPartner: [family], people: [ewan, ishbel] })
    await openEwan()

    click('Remove partnership with Ishbel Campbell')
    click('Cancel')

    expect(screen.queryByRole('button', { name: 'Yes, remove' })).not.toBeInTheDocument()
  })

  describe('when the relative is not in the tree yet', () => {
    async function startAddingSomeone() {
      await openEwan()
      searchFinds()
      click('Add a partner')
      click('Add someone new')
    }

    it('adds them and chooses them, ready to link', async () => {
      let sent: unknown
      let linked: unknown
      await startAddingSomeone()
      server.use(
        http.post('*/api/v1/admin/people', async ({ request }) => {
          sent = await request.json()
          return HttpResponse.json(rory, { status: 201 })
        }),
        http.post('*/api/v1/admin/families', async ({ request }) => {
          linked = await request.json()
          return HttpResponse.json({}, { status: 201 })
        }),
      )

      type('Given name', 'Rory')
      type('Surname', 'MacLeod')
      type('Sex', 'Male')
      click('Add to the tree')

      expect(await screen.findByText('Chosen:')).toHaveTextContent('Rory MacLeod')
      expect(screen.queryByLabelText('Given name')).not.toBeInTheDocument()
      expect(sent).toEqual({ sex: 'Male', names: [{ givenName: 'Rory', surname: 'MacLeod', type: 'Birth' }] })
      click('Save')
      await waitFor(() => expect(linked).toEqual({ partner1Id: 'ewan', partner2Id: 'rory', type: 'Marriage' }))
    })

    it('starts from the name that was searched for', async () => {
      await openEwan()
      searchFinds()
      click('Add a partner')

      type('Find someone', 'Rory Mac Leod')
      click('Add someone new')

      expect(screen.getByLabelText('Given name')).toHaveValue('Rory')
      expect(screen.getByLabelText('Surname')).toHaveValue('Mac Leod')
    })

    it('can use a person who turns out to be there already', async () => {
      let linked: unknown
      await startAddingSomeone()
      server.use(
        http.post('*/api/v1/admin/people', () =>
          HttpResponse.json({ title: 'This may be a duplicate', matches: [{ ...rory, events: morag.events }] }, { status: 409, ...problemJson }),
        ),
        http.post('*/api/v1/admin/families', async ({ request }) => {
          linked = await request.json()
          return HttpResponse.json({}, { status: 201 })
        }),
      )

      type('Given name', 'Rory')
      click('Add to the tree')
      fireEvent.click(await screen.findByRole('button', { name: /Use Rory MacLeod b\. c\. 1870/ }))

      expect(await screen.findByText('Chosen:')).toHaveTextContent('Rory MacLeod')
      click('Save')
      await waitFor(() => expect(linked).toEqual({ partner1Id: 'ewan', partner2Id: 'rory', type: 'Marriage' }))
    })

    it('can add a different person who shares the name', async () => {
      const searches: string[] = []
      await startAddingSomeone()
      server.use(
        http.post('*/api/v1/admin/people', ({ request }) => {
          const { search } = new URL(request.url)
          searches.push(search)
          return search
            ? HttpResponse.json(rory, { status: 201 })
            : HttpResponse.json({ matches: [{ ...rory, id: 'other' }] }, { status: 409, ...problemJson })
        }),
      )

      type('Given name', 'Rory')
      click('Add to the tree')
      fireEvent.click(await screen.findByRole('button', { name: "It's a different person: add anyway" }))

      expect(await screen.findByText('Chosen:')).toHaveTextContent('Rory MacLeod')
      expect(searches).toEqual(['', '?allowDuplicate=true'])
    })

    it('never offers the person themselves as the match', async () => {
      await startAddingSomeone()
      server.use(
        http.post('*/api/v1/admin/people', () =>
          HttpResponse.json({ matches: [ewan, { ...ewan, id: 'other-ewan' }] }, { status: 409, ...problemJson }),
        ),
      )

      type('Given name', 'Ewan')
      click('Add to the tree')

      expect(await screen.findAllByRole('button', { name: /^Use Ewan MacLeod/ })).toHaveLength(1)
    })

    it('shows the reason next to the name when it is refused', async () => {
      await startAddingSomeone()
      server.use(
        http.post('*/api/v1/admin/people', () =>
          HttpResponse.json({ errors: { 'names[0].givenName': ['A given name is required.'] } }, { status: 400, ...problemJson }),
        ),
      )

      click('Add to the tree')

      expect(await screen.findByRole('alert')).toHaveTextContent('A given name is required.')
      expect(screen.getByLabelText('Given name')).toHaveAttribute('aria-invalid', 'true')
    })

    it('says so when adding fails for another reason', async () => {
      await startAddingSomeone()
      server.use(http.post('*/api/v1/admin/people', () => new HttpResponse(null, { status: 500 })))

      click('Add to the tree')

      expect(await screen.findByRole('alert')).toHaveTextContent("This person couldn't be added.")
    })

    it('can back out without adding anyone', async () => {
      await startAddingSomeone()

      click('Cancel adding')

      expect(screen.queryByLabelText('Given name')).not.toBeInTheDocument()
      expect(screen.getByRole('button', { name: 'Save' })).toBeDisabled()
    })
  })

  it('can back out of adding a relative', async () => {
    await openEwan()

    click('Add a partner')
    click('Cancel')

    expect(screen.queryByRole('form', { name: 'Add a partner' })).not.toBeInTheDocument()
  })
})
