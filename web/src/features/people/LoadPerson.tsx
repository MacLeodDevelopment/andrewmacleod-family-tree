import { useQuery } from '@tanstack/react-query'
import type { ReactNode } from 'react'
import { Link, useParams } from 'react-router'
import { ApiError, useApi } from '../../shared/api/client.ts'
import { peopleUrl, type Person } from './people.ts'

/** Loads the person named in the address, showing progress or what went wrong until they arrive. */
export function LoadPerson({ children }: { children: (person: Person) => ReactNode }) {
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

  return children(person.data)
}
