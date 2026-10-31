import { useQuery } from '@tanstack/react-query'
import { useState } from 'react'
import { Link } from 'react-router'
import { useApi } from '../../shared/api/client.ts'
import { useDebounced } from '../../shared/useDebounced.ts'
import { profilesUrl, summaryDates, type SearchResults } from './profile.ts'

const searchDelayMs = 250
const minimumSearchLength = 2

/** Finds anyone in the tree by any of their names. */
export function SearchPage() {
  const api = useApi()
  const [text, setText] = useState('')
  const search = useDebounced(text.trim(), searchDelayMs)

  const results = useQuery({
    queryKey: ['search', search],
    queryFn: () => api<SearchResults>(`${profilesUrl}/search?q=${encodeURIComponent(search)}`),
    enabled: search.length >= minimumSearchLength,
  })

  const items = results.data?.items ?? []
  const total = results.data?.total ?? 0

  return (
    <>
      <h1>Search</h1>

      <div className="form">
        <label>
          Name
          <input type="search" value={text} onChange={(event) => setText(event.target.value)} autoComplete="off" />
        </label>
      </div>

      {results.isError && <p role="alert">The search didn't work. Please try again.</p>}
      {results.isSuccess && items.length === 0 && <p role="status">Nobody found.</p>}

      {items.length > 0 && (
        <ul className="people-list">
          {items.map((person) => (
            <li key={person.id}>
              <Link to={`/family/${person.id}`}>{person.name}</Link>
              <span className="people-list-dates">{summaryDates(person)}</span>
            </li>
          ))}
        </ul>
      )}

      {total > items.length && (
        <p className="people-count">
          Showing {items.length} of {total}. Type more of the name to narrow it down.
        </p>
      )}
    </>
  )
}
