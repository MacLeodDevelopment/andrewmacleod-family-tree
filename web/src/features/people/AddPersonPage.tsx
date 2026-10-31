import { useMutation, useQueryClient } from '@tanstack/react-query'
import { useState, type FormEvent } from 'react'
import { Link, useNavigate } from 'react-router'
import { ApiError, useApi } from '../../shared/api/client.ts'
import { FieldError } from './FieldError.tsx'
import { nameTypes, peopleUrl, sexes, type NameType, type Person, type Sex } from './people.ts'

interface NameRow {
  givenName: string
  surname: string
  type: NameType
}

const emptyName: NameRow = { givenName: '', surname: '', type: 'Birth' }

export function AddPersonPage() {
  const api = useApi()
  const navigate = useNavigate()
  const queryClient = useQueryClient()
  const [names, setNames] = useState<NameRow[]>([emptyName])
  const [sex, setSex] = useState<Sex>('Unknown')

  const addPerson = useMutation({
    mutationFn: (allowDuplicate: boolean) =>
      api<Person>(`${peopleUrl}${allowDuplicate ? '?allowDuplicate=true' : ''}`, {
        method: 'POST',
        body: JSON.stringify({ sex, names: names.map((name) => ({ ...name, surname: name.surname || null })) }),
      }),
    onSuccess: async (person) => {
      await queryClient.invalidateQueries({ queryKey: ['people'] })
      await navigate(`/people/${person.id}`)
    },
  })

  const problem = addPerson.error instanceof ApiError ? addPerson.error.problem : undefined
  const duplicates = problem?.duplicates ?? []
  const updateName = (index: number, change: Partial<NameRow>) =>
    setNames(names.map((name, i) => (i === index ? { ...name, ...change } : name)))

  const submit = (event: FormEvent) => {
    event.preventDefault()
    addPerson.mutate(false)
  }

  return (
    <>
      <h1>Add a person</h1>

      <form className="form" onSubmit={submit} noValidate>
        {names.map((name, index) => (
          <fieldset key={index}>
            <legend>{index === 0 ? 'Main name' : `Other name ${index}`}</legend>

            <label>
              Given name
              <input
                value={name.givenName}
                onChange={(event) => updateName(index, { givenName: event.target.value })}
                aria-invalid={Boolean(problem?.errors?.[`names[${index}].givenName`])}
              />
            </label>
            <FieldError messages={problem?.errors?.[`names[${index}].givenName`]} />

            <label>
              Surname
              <input value={name.surname} onChange={(event) => updateName(index, { surname: event.target.value })} />
            </label>
            <FieldError messages={problem?.errors?.[`names[${index}].surname`]} />

            <label>
              Type of name
              <select value={name.type} onChange={(event) => updateName(index, { type: event.target.value as NameType })}>
                {nameTypes.map((type) => (
                  <option key={type}>{type}</option>
                ))}
              </select>
            </label>

            {index > 0 && (
              <button type="button" onClick={() => setNames(names.filter((_, i) => i !== index))}>
                Remove this name
              </button>
            )}
          </fieldset>
        ))}

        <button type="button" onClick={() => setNames([...names, { ...emptyName, type: 'Married' }])}>
          Add another name
        </button>
        <FieldError messages={problem?.errors?.names} />

        <label>
          Sex
          <select value={sex} onChange={(event) => setSex(event.target.value as Sex)}>
            {sexes.map((option) => (
              <option key={option}>{option}</option>
            ))}
          </select>
        </label>

        {duplicates.length > 0 && (
          <div role="alert" className="notice">
            <p>
              <strong>This may be a duplicate.</strong> Someone with this name already exists:
            </p>
            <ul>
              {duplicates.map((id, index) => (
                <li key={id}>
                  <Link to={`/people/${id}`}>See match {index + 1}</Link>
                </li>
              ))}
            </ul>
            <button type="button" disabled={addPerson.isPending} onClick={() => addPerson.mutate(true)}>
              It's a different person: add anyway
            </button>
          </div>
        )}

        {addPerson.isError && !problem?.errors && duplicates.length === 0 && (
          <p role="alert" className="field-error">
            This person couldn't be saved. Please try again.
          </p>
        )}

        <div className="form-buttons">
          <button type="submit" disabled={addPerson.isPending}>
            Save
          </button>
          <Link to="/people">Cancel</Link>
        </div>
      </form>
    </>
  )
}
