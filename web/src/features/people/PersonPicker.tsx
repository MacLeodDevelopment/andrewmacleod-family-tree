import { useQuery } from '@tanstack/react-query'
import { useEffect, useState } from 'react'
import { useApi } from '../../shared/api/client.ts'
import { fullName, lifeDates, peopleUrl, type PeoplePage } from './people.ts'

const searchDelayMs = 250
const minimumSearchLength = 2
const maximumResults = 10

interface PersonPickerProps {
  /** The person whose page this is, who can't be their own relative. */
  excludeId: string
  chosenId: string | null
  onChoose: (id: string) => void
}

/** Finds someone already in the tree by name. */
export function PersonPicker({ excludeId, chosenId, onChoose }: PersonPickerProps) {
  const api = useApi()
  const [text, setText] = useState('')
  const [search, setSearch] = useState('')

  // Wait for a pause in the typing, so each keystroke doesn't use up the rate limit.
  useEffect(() => {
    const timer = setTimeout(() => setSearch(text.trim()), searchDelayMs)
    return () => clearTimeout(timer)
  }, [text])

  const results = useQuery({
    queryKey: ['people', 'search', search],
    queryFn: () => api<PeoplePage>(`${peopleUrl}?take=${maximumResults}&search=${encodeURIComponent(search)}`),
    enabled: search.length >= minimumSearchLength,
  })

  const matches = results.data?.items.filter((person) => person.id !== excludeId) ?? []

  return (
    <>
      <label>
        Find someone
        <input value={text} onChange={(event) => setText(event.target.value)} autoComplete="off" />
      </label>

      {results.isError && (
        <p role="alert" className="field-error">
          The search didn't work. Please try again.
        </p>
      )}

      {results.isSuccess && matches.length === 0 && <p role="status">Nobody found. Add them from the People page first.</p>}

      {matches.length > 0 && (
        <fieldset className="picker">
          <legend className="sr-only">Matching people</legend>
          {matches.map((person) => (
            <label key={person.id}>
              <input
                type="radio"
                name="person"
                checked={chosenId === person.id}
                onChange={() => onChoose(person.id)}
              />
              <span>
                {fullName(person)} <span className="people-list-dates">{lifeDates(person)}</span>
              </span>
            </label>
          ))}
          {(results.data?.total ?? 0) > maximumResults && (
            <p className="field-hint">Showing the first {maximumResults}. Type more of the name to narrow it down.</p>
          )}
        </fieldset>
      )}
    </>
  )
}
