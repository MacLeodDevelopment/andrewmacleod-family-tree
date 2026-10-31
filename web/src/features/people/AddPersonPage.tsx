import { useMutation, useQueryClient } from '@tanstack/react-query'
import { Link, useNavigate } from 'react-router'
import { ApiError, useApi } from '../../shared/api/client.ts'
import { DuplicateNotice } from './DuplicateNotice.tsx'
import { PersonForm } from './PersonForm.tsx'
import { duplicateMatches, fullName, lifeDates, peopleUrl, type Person, type PersonDetails } from './people.ts'

interface NewPerson {
  details: PersonDetails
  allowDuplicate: boolean
}

export function AddPersonPage() {
  const api = useApi()
  const navigate = useNavigate()
  const queryClient = useQueryClient()

  const addPerson = useMutation({
    mutationFn: ({ details, allowDuplicate }: NewPerson) =>
      api<Person>(`${peopleUrl}${allowDuplicate ? '?allowDuplicate=true' : ''}`, {
        method: 'POST',
        body: JSON.stringify(details),
      }),
    onSuccess: async (person) => {
      await queryClient.invalidateQueries({ queryKey: ['people'] })
      await navigate(`/people/${person.id}`)
    },
  })

  const problem = addPerson.error instanceof ApiError ? addPerson.error.problem : undefined
  const duplicates = duplicateMatches(problem)

  const renderNotice = (details: PersonDetails) => (
    <>
      {duplicates.length > 0 && (
        <DuplicateNotice
          matches={duplicates}
          renderMatch={(person) => (
            <>
              <Link to={`/people/${person.id}`}>{fullName(person)}</Link>{' '}
              <span className="people-list-dates">{lifeDates(person)}</span>
            </>
          )}
          adding={addPerson.isPending}
          onAddAnyway={() => addPerson.mutate({ details, allowDuplicate: true })}
        />
      )}

      {addPerson.isError && !problem?.errors && duplicates.length === 0 && (
        <p role="alert" className="field-error">
          This person couldn't be saved. Please try again.
        </p>
      )}
    </>
  )

  return (
    <>
      <h1>Add a person</h1>
      <PersonForm
        problem={problem}
        saving={addPerson.isPending}
        cancelTo="/people"
        onSave={(details) => addPerson.mutate({ details, allowDuplicate: false })}
        renderNotice={renderNotice}
      />
    </>
  )
}
