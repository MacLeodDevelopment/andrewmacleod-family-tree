import { useMutation, useQueryClient } from '@tanstack/react-query'
import { useNavigate } from 'react-router'
import { ApiError, useApi } from '../../shared/api/client.ts'
import { LoadPerson } from './LoadPerson.tsx'
import { PersonForm } from './PersonForm.tsx'
import { fullName, peopleUrl, refreshPerson, type Person, type PersonDetails } from './people.ts'

export function EditPersonPage() {
  return <LoadPerson>{(person) => <EditPerson person={person} />}</LoadPerson>
}

function EditPerson({ person }: { person: Person }) {
  const api = useApi()
  const navigate = useNavigate()
  const queryClient = useQueryClient()
  const personUrl = `/people/${person.id}`

  const save = useMutation({
    mutationFn: (details: PersonDetails) =>
      api<Person>(`${peopleUrl}/${person.id}`, { method: 'PUT', body: JSON.stringify(details) }),
    onSuccess: async () => {
      await refreshPerson(queryClient, person.id)
      await navigate(personUrl)
    },
  })

  const problem = save.error instanceof ApiError ? save.error.problem : undefined

  return (
    <>
      <h1>Edit {fullName(person)}</h1>
      <PersonForm
        person={person}
        problem={problem}
        saving={save.isPending}
        cancelTo={personUrl}
        onSave={(details) => save.mutate(details)}
        renderNotice={() =>
          save.isError &&
          !problem?.errors && (
            <p role="alert" className="field-error">
              The changes couldn't be saved. Please try again.
            </p>
          )
        }
      />
    </>
  )
}
