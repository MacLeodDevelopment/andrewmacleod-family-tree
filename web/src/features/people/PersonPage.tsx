import { useMutation, useQueryClient } from '@tanstack/react-query'
import { useState } from 'react'
import { Link, useNavigate } from 'react-router'
import { ApiError, useApi } from '../../shared/api/client.ts'
import { LifeEvents } from './LifeEvents.tsx'
import { LoadPerson } from './LoadPerson.tsx'
import { fullName, peopleUrl, type Person } from './people.ts'
import { Relatives } from './Relatives.tsx'

export function PersonPage() {
  return <LoadPerson>{(person) => <PersonDetails person={person} />}</LoadPerson>
}

function PersonDetails({ person }: { person: Person }) {
  return (
    <>
      <p className="form-buttons">
        <Link to="/people">Back to people</Link>
        <Link to={`/family/${person.id}`}>See profile</Link>
      </p>
      <div className="page-heading">
        <h1>{fullName(person)}</h1>
        <Link className="button" to={`/people/${person.id}/edit`}>
          Edit
        </Link>
      </div>

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

      <Relatives person={person} />

      <LifeEvents person={person} />

      <DeletePerson person={person} />
    </>
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
