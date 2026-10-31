import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useEffect, useState } from 'react'
import { ApiError, useApi } from '../../shared/api/client.ts'
import { DuplicateNotice } from './DuplicateNotice.tsx'
import { FieldError } from './FieldError.tsx'
import { duplicateMatches, fullName, lifeDates, peopleUrl, sexes, type PeoplePage, type Person, type Sex } from './people.ts'

const searchDelayMs = 250
const minimumSearchLength = 2
const maximumResults = 10

interface PersonPickerProps {
  /** The person whose page this is, who can't be their own relative. */
  excludeId: string
  chosenId: string | null
  onChoose: (id: string) => void
}

/** Finds someone already in the tree by name, or adds them if they aren't there yet. */
export function PersonPicker({ excludeId, chosenId, onChoose }: PersonPickerProps) {
  const api = useApi()
  const [text, setText] = useState('')
  const [search, setSearch] = useState('')
  const [addingNew, setAddingNew] = useState(false)
  // Someone just added or matched, who a search wouldn't be showing.
  const [justChosen, setJustChosen] = useState<Person | null>(null)

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

  const choose = (person: Person) => {
    setJustChosen(person)
    setAddingNew(false)
    onChoose(person.id)
  }

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

      {results.isSuccess && matches.length === 0 && <p role="status">Nobody found.</p>}

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

      {justChosen?.id === chosenId && (
        <p role="status">
          Chosen: <strong>{fullName(justChosen)}</strong>
        </p>
      )}

      {addingNew ? (
        <NewPerson
          startingName={text.trim()}
          excludeId={excludeId}
          onAdded={choose}
          onCancel={() => setAddingNew(false)}
        />
      ) : (
        <div>
          <button type="button" className="small" onClick={() => setAddingNew(true)}>
            Add someone new
          </button>
        </div>
      )}
    </>
  )
}

interface NewPersonProps {
  /** What was searched for: the first word is taken as the given name, the rest as the surname. */
  startingName: string
  excludeId: string
  onAdded: (person: Person) => void
  onCancel: () => void
}

/** Adds someone to the tree without leaving the page. It sits inside a form, so it has no form of its own. */
function NewPerson({ startingName, excludeId, onAdded, onCancel }: NewPersonProps) {
  const api = useApi()
  const queryClient = useQueryClient()
  const [firstWord = '', ...otherWords] = startingName.split(/\s+/)
  const [givenName, setGivenName] = useState(firstWord)
  const [surname, setSurname] = useState(otherWords.join(' '))
  const [sex, setSex] = useState<Sex>('Unknown')

  const add = useMutation({
    mutationFn: (allowDuplicate: boolean) =>
      api<Person>(`${peopleUrl}${allowDuplicate ? '?allowDuplicate=true' : ''}`, {
        method: 'POST',
        body: JSON.stringify({ sex, names: [{ givenName, surname: surname || null, type: 'Birth' }] }),
      }),
    onSuccess: async (person) => {
      await queryClient.invalidateQueries({ queryKey: ['people'] })
      onAdded(person)
    },
  })

  const problem = add.error instanceof ApiError ? add.error.problem : undefined
  const duplicates = duplicateMatches(problem).filter((person) => person.id !== excludeId)

  return (
    <fieldset>
      <legend>Add someone new</legend>

      <label>
        Given name
        <input
          value={givenName}
          onChange={(event) => setGivenName(event.target.value)}
          aria-invalid={Boolean(problem?.errors?.['names[0].givenName'])}
        />
      </label>
      <FieldError messages={problem?.errors?.['names[0].givenName']} />

      <label>
        Surname
        <input value={surname} onChange={(event) => setSurname(event.target.value)} />
      </label>
      <FieldError messages={problem?.errors?.['names[0].surname']} />

      <label>
        Sex
        <select value={sex} onChange={(event) => setSex(event.target.value as Sex)}>
          {sexes.map((option) => (
            <option key={option}>{option}</option>
          ))}
        </select>
      </label>

      {problem?.matches ? (
        <DuplicateNotice
          matches={duplicates}
          renderMatch={(person) => (
            <button type="button" className="small" onClick={() => onAdded(person)}>
              Use {fullName(person)} {lifeDates(person)}
            </button>
          )}
          adding={add.isPending}
          onAddAnyway={() => add.mutate(true)}
        />
      ) : (
        add.isError &&
        !problem?.errors && (
          <p role="alert" className="field-error">
            This person couldn't be added. Please try again.
          </p>
        )
      )}

      <div className="form-buttons">
        <button type="button" disabled={add.isPending} onClick={() => add.mutate(false)}>
          Add to the tree
        </button>
        <button type="button" onClick={onCancel}>
          Cancel adding
        </button>
      </div>
    </fieldset>
  )
}
