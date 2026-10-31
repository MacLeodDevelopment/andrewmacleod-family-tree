import { useMutation, useQueryClient } from '@tanstack/react-query'
import { useState, type FormEvent } from 'react'
import { ApiError, useApi } from '../../shared/api/client.ts'
import { FieldError } from './FieldError.tsx'
import { eventTypes, peopleUrl, refreshPerson, type EventType, type LifeEvent, type Person } from './people.ts'

/** A person's timeline, and everything that changes it. */
export function LifeEvents({ person }: { person: Person }) {
  const eventsLeftToAdd = eventTypes.filter((type) => !person.events.some((event) => event.type === type))

  return (
    <>
      <h2>Life events</h2>
      {person.events.length === 0 ? (
        <p>No events recorded yet.</p>
      ) : (
        <ul className="timeline">
          {person.events.map((event) => (
            <EventRow key={event.id} personId={person.id} event={event} />
          ))}
        </ul>
      )}

      {eventsLeftToAdd.length > 0 && <AddEventForm personId={person.id} types={eventsLeftToAdd} />}
    </>
  )
}

function EventRow({ personId, event }: { personId: string; event: LifeEvent }) {
  const [mode, setMode] = useState<'reading' | 'editing' | 'removing'>('reading')
  const name = event.type.toLowerCase()

  if (mode === 'editing') {
    return (
      <li>
        <EditEventForm personId={personId} event={event} onDone={() => setMode('reading')} />
      </li>
    )
  }

  return (
    <li>
      <div className="timeline-row">
        <span>
          <strong>{event.type}</strong>
          {[event.date, event.place].filter(Boolean).map((detail) => ` · ${detail}`)}
        </span>
        {mode === 'reading' && (
          <span className="form-buttons">
            <button type="button" className="small" aria-label={`Edit ${name}`} onClick={() => setMode('editing')}>
              Edit
            </button>
            <button type="button" className="small" aria-label={`Remove ${name}`} onClick={() => setMode('removing')}>
              Remove
            </button>
          </span>
        )}
      </div>
      {mode === 'removing' && <RemoveEvent personId={personId} event={event} onCancel={() => setMode('reading')} />}
    </li>
  )
}

function RemoveEvent({ personId, event, onCancel }: { personId: string; event: LifeEvent; onCancel: () => void }) {
  const api = useApi()
  const queryClient = useQueryClient()

  const remove = useMutation({
    mutationFn: () => api<void>(`${peopleUrl}/${personId}/events/${event.id}`, { method: 'DELETE' }),
    onSuccess: () => refreshPerson(queryClient, personId),
  })

  return (
    <div className="notice timeline-notice">
      <p>Remove this {event.type.toLowerCase()}? This can't be undone.</p>
      {remove.isError && (
        <p role="alert" className="field-error">
          This event couldn't be removed. Please try again.
        </p>
      )}
      <div className="form-buttons">
        <button type="button" disabled={remove.isPending} onClick={() => remove.mutate()}>
          Yes, remove
        </button>
        <button type="button" onClick={onCancel}>
          Cancel
        </button>
      </div>
    </div>
  )
}

function EditEventForm({ personId, event, onDone }: { personId: string; event: LifeEvent; onDone: () => void }) {
  const api = useApi()
  const queryClient = useQueryClient()
  const [date, setDate] = useState(event.dateAsEntered ?? '')
  const [place, setPlace] = useState(event.place ?? '')

  const save = useMutation({
    mutationFn: () =>
      api<Person>(`${peopleUrl}/${personId}/events/${event.id}`, {
        method: 'PUT',
        body: JSON.stringify({ date: date || null, place: place || null }),
      }),
    onSuccess: async () => {
      await refreshPerson(queryClient, personId)
      onDone()
    },
  })

  const submit = (formEvent: FormEvent) => {
    formEvent.preventDefault()
    save.mutate()
  }

  return (
    <form className="form" aria-label={`Edit ${event.type.toLowerCase()}`} onSubmit={submit} noValidate>
      <strong>{event.type}</strong>

      <EventFields date={date} place={place} setDate={setDate} setPlace={setPlace} error={save.error} />

      <div className="form-buttons">
        <button type="submit" disabled={save.isPending}>
          Save
        </button>
        <button type="button" onClick={onDone}>
          Cancel
        </button>
      </div>
    </form>
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
      await refreshPerson(queryClient, personId)
    },
  })

  const typeErrors = addEvent.error instanceof ApiError ? addEvent.error.problem.errors?.type : undefined
  const submit = (event: FormEvent) => {
    event.preventDefault()
    addEvent.mutate()
  }

  return (
    <form className="form" aria-label="Add an event" onSubmit={submit} noValidate>
      <h2>Add an event</h2>

      <label>
        Event
        <select value={type} onChange={(event) => setChosenType(event.target.value as EventType)}>
          {types.map((option) => (
            <option key={option}>{option}</option>
          ))}
        </select>
      </label>
      <FieldError messages={typeErrors} />

      <EventFields date={date} place={place} setDate={setDate} setPlace={setPlace} error={addEvent.error} />

      <div className="form-buttons">
        <button type="submit" disabled={addEvent.isPending}>
          Add event
        </button>
      </div>
    </form>
  )
}

interface EventFieldsProps {
  date: string
  place: string
  setDate: (date: string) => void
  setPlace: (place: string) => void
  error: Error | null
}

/** The date and place of an event, with anything the API said about them. */
function EventFields({ date, place, setDate, setPlace, error }: EventFieldsProps) {
  const errors = error instanceof ApiError ? error.problem.errors : undefined

  return (
    <>
      <label>
        Date
        <input value={date} onChange={(event) => setDate(event.target.value)} aria-invalid={Boolean(errors?.date)} />
      </label>
      <p className="field-hint">As the records give it: 12 March 1901, abt 1870, bef 1901, 1650/51. Leave blank if unknown.</p>
      <FieldError messages={errors?.date} />

      <label>
        Place
        <input value={place} onChange={(event) => setPlace(event.target.value)} />
      </label>
      <FieldError messages={errors?.place} />

      {error && !errors && (
        <p role="alert" className="field-error">
          This event couldn't be saved. Please try again.
        </p>
      )}
    </>
  )
}
