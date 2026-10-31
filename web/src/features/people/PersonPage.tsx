import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useState, type FormEvent } from 'react'
import { Link, useNavigate, useParams } from 'react-router'
import { ApiError, useApi } from '../../shared/api/client.ts'
import { FieldError } from './FieldError.tsx'
import { eventTypes, fullName, peopleUrl, type EventType, type Person } from './people.ts'

export function PersonPage() {
  const { id } = useParams()
  const api = useApi()
  const person = useQuery({
    queryKey: ['person', id],
    queryFn: () => api<Person>(`${peopleUrl}/${id}`),
  })

  if (person.isPending) {
    return <p role="status">Loading…</p>
  }

  if (person.isError) {
    const notFound = person.error instanceof ApiError && person.error.status === 404
    return (
      <>
        <p role="alert">{notFound ? "This person doesn't exist." : "This person couldn't be loaded. Please try again."}</p>
        <Link to="/people">Back to people</Link>
      </>
    )
  }

  return <PersonDetails person={person.data} />
}

function PersonDetails({ person }: { person: Person }) {
  const eventsLeftToAdd = eventTypes.filter((type) => !person.events.some((event) => event.type === type))

  return (
    <>
      <p>
        <Link to="/people">Back to people</Link>
      </p>
      <h1>{fullName(person)}</h1>

      <dl className="person-facts">
        <dt>Sex</dt>
        <dd>{person.sex}</dd>
        {person.names.length > 1 && (
          <>
            <dt>Names</dt>
            {person.names.map((name, index) => (
              <dd key={index}>
                {[name.givenName, name.surname].filter(Boolean).join(' ')} ({name.type.toLowerCase()})
              </dd>
            ))}
          </>
        )}
      </dl>

      <h2>Life events</h2>
      {person.events.length === 0 ? (
        <p>No events recorded yet.</p>
      ) : (
        <ul className="timeline">
          {person.events.map((event) => (
            <li key={event.id}>
              <strong>{event.type}</strong>
              {[event.date, event.place].filter(Boolean).map((detail) => ` · ${detail}`)}
            </li>
          ))}
        </ul>
      )}

      {eventsLeftToAdd.length > 0 && <AddEventForm personId={person.id} types={eventsLeftToAdd} />}

      <DeletePerson person={person} />
    </>
  )
}

function AddEventForm({ personId, types }: { personId: string; types: EventType[] }) {
  const api = useApi()
  const queryClient = useQueryClient()
  const [chosenType, setChosenType] = useState<EventType | null>(null)
  const [date, setDate] = useState('')
  const [place, setPlace] = useState('')
  // After a birth is saved it's no longer on offer, so fall back to what's left.
  const type = chosenType && types.includes(chosenType) ? chosenType : types[0]

  const addEvent = useMutation({
    mutationFn: () =>
      api<Person>(`${peopleUrl}/${personId}/events`, {
        method: 'POST',
        body: JSON.stringify({ type, date: date || null, place: place || null }),
      }),
    onSuccess: async () => {
      setDate('')
      setPlace('')
      await queryClient.invalidateQueries({ queryKey: ['person', personId] })
      await queryClient.invalidateQueries({ queryKey: ['people'] })
    },
  })

  const problem = addEvent.error instanceof ApiError ? addEvent.error.problem : undefined
  const submit = (event: FormEvent) => {
    event.preventDefault()
    addEvent.mutate()
  }

  return (
    <form className="form" onSubmit={submit} noValidate>
      <h2>Add an event</h2>

      <label>
        Event
        <select value={type} onChange={(event) => setChosenType(event.target.value as EventType)}>
          {types.map((option) => (
            <option key={option}>{option}</option>
          ))}
        </select>
      </label>
      <FieldError messages={problem?.errors?.type} />

      <label>
        Date
        <input value={date} onChange={(event) => setDate(event.target.value)} aria-invalid={Boolean(problem?.errors?.date)} />
      </label>
      <p className="field-hint">As the records give it: 12 March 1901, abt 1870, bef 1901, 1650/51. Leave blank if unknown.</p>
      <FieldError messages={problem?.errors?.date} />

      <label>
        Place
        <input value={place} onChange={(event) => setPlace(event.target.value)} />
      </label>
      <FieldError messages={problem?.errors?.place} />

      {addEvent.isError && !problem?.errors && (
        <p role="alert" className="field-error">
          This event couldn't be saved. Please try again.
        </p>
      )}

      <div className="form-buttons">
        <button type="submit" disabled={addEvent.isPending}>
          Add event
        </button>
      </div>
    </form>
  )
}

function DeletePerson({ person }: { person: Person }) {
  const api = useApi()
  const navigate = useNavigate()
  const queryClient = useQueryClient()
  const [confirming, setConfirming] = useState(false)

  const deletePerson = useMutation({
    mutationFn: () => api<void>(`${peopleUrl}/${person.id}`, { method: 'DELETE' }),
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: ['people'] })
      await navigate('/people')
    },
  })

  if (!confirming) {
    return (
      <div className="danger-zone">
        <button type="button" onClick={() => setConfirming(true)}>
          Delete this person
        </button>
      </div>
    )
  }

  return (
    <div className="danger-zone notice">
      <p>
        Delete <strong>{fullName(person)}</strong> and their life events? This can't be undone.
      </p>
      {deletePerson.isError && (
        <p role="alert" className="field-error">
          {deletePerson.error instanceof ApiError && deletePerson.error.status === 409
            ? 'This person is part of a family. Remove them from their families first.'
            : "This person couldn't be deleted. Please try again."}
        </p>
      )}
      <div className="form-buttons">
        <button type="button" disabled={deletePerson.isPending} onClick={() => deletePerson.mutate()}>
          Yes, delete
        </button>
        <button type="button" onClick={() => setConfirming(false)}>
          Cancel
        </button>
      </div>
    </div>
  )
}
