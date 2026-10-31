import { useQuery } from '@tanstack/react-query'
import { useState } from 'react'
import { Link } from 'react-router'
import { useApi } from '../../shared/api/client.ts'
import { fullName, lifeDates, peopleUrl, type PeoplePage } from './people.ts'

const pageSize = 50

export function PeopleListPage() {
  const api = useApi()
  const [skip, setSkip] = useState(0)
  const people = useQuery({
    queryKey: ['people', skip],
    queryFn: () => api<PeoplePage>(`${peopleUrl}?skip=${skip}&take=${pageSize}`),
  })

  return (
    <>
      <div className="page-heading">
        <h1>People</h1>
        <span className="form-buttons">
          <Link to="/people/accounts">Linked accounts</Link>
          <Link className="button" to="/people/new">
            Add a person
          </Link>
        </span>
      </div>

      {people.isPending && <p role="status">Loading…</p>}
      {people.isError && <p role="alert">The list of people couldn't be loaded. Please try again.</p>}

      {people.data?.total === 0 && <p>Nobody has been added yet.</p>}

      {people.data && people.data.items.length > 0 && (
        <>
          <ul className="people-list">
            {people.data.items.map((person) => (
              <li key={person.id}>
                <Link to={`/people/${person.id}`}>{fullName(person)}</Link>
                <span className="people-list-dates">{lifeDates(person)}</span>
              </li>
            ))}
          </ul>

          <p className="people-count">
            {people.data.total === 1 ? '1 person' : `${people.data.total} people`}
          </p>

          {people.data.total > pageSize && (
            <div className="page-buttons">
              <button type="button" disabled={skip === 0} onClick={() => setSkip(skip - pageSize)}>
                Previous
              </button>
              <button type="button" disabled={skip + pageSize >= people.data.total} onClick={() => setSkip(skip + pageSize)}>
                Next
              </button>
            </div>
          )}
        </>
      )}
    </>
  )
}
