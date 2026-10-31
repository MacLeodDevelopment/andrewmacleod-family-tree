import { useMutation } from '@tanstack/react-query'
import { useState, type FormEvent } from 'react'
import { ApiError, useApi } from '../../shared/api/client.ts'
import { FieldError } from './FieldError.tsx'

export interface RecordedEvent {
  id: string
  type: string
  /** The date for reading, such as "c. 1870". */
  date: string | null
  /** The date as it was typed, such as "abt 1870". */
  dateAsEntered: string | null
  place: string | null
}

interface EventsEditorProps {
  events: RecordedEvent[]
  /** Every type of event this can hold. Each can happen once, so only those left are offered. */
  types: readonly string[]
  /** The address of these events in the API. */
  url: string
  /** Fetches whatever shows these events again, after a change. */
  refresh: () => Promise<unknown>
  /** Shown when there are no events. */
  emptyText?: string
  /** A button that opens the add form, rather than the form always showing. */
  addLabel?: string
  /** Tells the buttons apart for screen readers when a page has several sets of events. */
  labelSuffix?: string
}

/** A list of events that can be added to, corrected and removed. */
export function EventsEditor({ events, types, url, refresh, emptyText, addLabel, labelSuffix = '' }: EventsEditorProps) {
  const [adding, setAdding] = useState(false)
  const typesLeft = types.filter((type) => !events.some((event) => event.type === type))

  return (
    <>
      {events.length === 0 && emptyText && <p>{emptyText}</p>}
      {events.length > 0 && (
        <ul className="timeline">
          {events.map((event) => (
            <EventRow key={event.id} url={url} refresh={refresh} event={event} labelSuffix={labelSuffix} />
          ))}
        </ul>
      )}

      {typesLeft.length > 0 && !addLabel && <AddEventForm url={url} refresh={refresh} types={typesLeft} />}

      {typesLeft.length > 0 && addLabel && !adding && (
        <div className="form-buttons relatives-row-buttons">
          <button type="button" className="small" aria-label={`${addLabel}${labelSuffix}`} onClick={() => setAdding(true)}>
            {addLabel}
          </button>
        </div>
      )}
      {typesLeft.length > 0 && addLabel && adding && (
        <AddEventForm url={url} refresh={refresh} types={typesLeft} onClose={() => setAdding(false)} />
      )}
    </>
  )
}

interface EventRowProps {
  url: string
  refresh: () => Promise<unknown>
  event: RecordedEvent
  labelSuffix: string
}

function EventRow({ url, refresh, event, labelSuffix }: EventRowProps) {
  const [mode, setMode] = useState<'reading' | 'editing' | 'removing'>('reading')
  const name = `${event.type.toLowerCase()}${labelSuffix}`
  const eventUrl = `${url}/${event.id}`

  if (mode === 'editing') {
    return (
      <li>
        <EditEventForm url={eventUrl} refresh={refresh} event={event} name={name} onDone={() => setMode('reading')} />
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
      {mode === 'removing' && (
        <RemoveEvent url={eventUrl} refresh={refresh} type={event.type} onCancel={() => setMode('reading')} />
      )}
    </li>
  )
}

interface RemoveEventProps {
  url: string
  refresh: () => Promise<unknown>
  type: string
  onCancel: () => void
}

function RemoveEvent({ url, refresh, type, onCancel }: RemoveEventProps) {
  const api = useApi()

  const remove = useMutation({
    mutationFn: () => api<void>(url, { method: 'DELETE' }),
    onSuccess: refresh,
  })

  return (
    <div className="notice timeline-notice">
      <p>Remove this {type.toLowerCase()}? This can't be undone.</p>
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

interface EditEventFormProps {
  url: string
  refresh: () => Promise<unknown>
  event: RecordedEvent
  name: string
  onDone: () => void
}

function EditEventForm({ url, refresh, event, name, onDone }: EditEventFormProps) {
  const api = useApi()
  const [date, setDate] = useState(event.dateAsEntered ?? '')
  const [place, setPlace] = useState(event.place ?? '')

  const save = useMutation({
    mutationFn: () =>
      api<unknown>(url, {
        method: 'PUT',
        body: JSON.stringify({ date: date || null, place: place || null }),
      }),
    onSuccess: async () => {
      await refresh()
      onDone()
    },
  })

  const submit = (formEvent: FormEvent) => {
    formEvent.preventDefault()
    save.mutate()
  }

  return (
    <form className="form" aria-label={`Edit ${name}`} onSubmit={submit} noValidate>
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

interface AddEventFormProps {
  url: string
  refresh: () => Promise<unknown>
  types: string[]
  /** Given when the form was opened by a button, to close it again. */
  onClose?: () => void
}

function AddEventForm({ url, refresh, types, onClose }: AddEventFormProps) {
  const api = useApi()
  const [chosenType, setChosenType] = useState<string | null>(null)
  const [date, setDate] = useState('')
  const [place, setPlace] = useState('')
  // After a birth is saved it's no longer on offer, so fall back to what's left.
  const type = chosenType && types.includes(chosenType) ? chosenType : types[0]

  const addEvent = useMutation({
    mutationFn: () =>
      api<unknown>(url, {
        method: 'POST',
        body: JSON.stringify({ type, date: date || null, place: place || null }),
      }),
    onSuccess: async () => {
      setDate('')
      setPlace('')
      await refresh()
      onClose?.()
    },
  })

  const typeErrors = addEvent.error instanceof ApiError ? addEvent.error.problem.errors?.type : undefined
  const submit = (event: FormEvent) => {
    event.preventDefault()
    addEvent.mutate()
  }

  return (
    <form className="form" aria-label="Add an event" onSubmit={submit} noValidate>
      {onClose ? <h3>Add an event</h3> : <h2>Add an event</h2>}

      <label>
        Event
        <select value={type} onChange={(event) => setChosenType(event.target.value)}>
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
        {onClose && (
          <button type="button" onClick={onClose}>
            Cancel
          </button>
        )}
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
