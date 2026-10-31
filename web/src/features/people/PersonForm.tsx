import { useState, type FormEvent, type ReactNode } from 'react'
import { Link } from 'react-router'
import type { Problem } from '../../shared/api/client.ts'
import { FieldError } from './FieldError.tsx'
import { nameTypes, sexes, type NameType, type Person, type PersonDetails, type Sex } from './people.ts'

interface NameRow {
  givenName: string
  surname: string
  type: NameType
}

const emptyName: NameRow = { givenName: '', surname: '', type: 'Birth' }

interface PersonFormProps {
  /** Who's being edited; leave out to start with a blank form. */
  person?: Person
  problem: Problem | undefined
  saving: boolean
  cancelTo: string
  onSave: (details: PersonDetails) => void
  /** Extra content above the buttons, given the details as they stand. */
  renderNotice?: (details: PersonDetails) => ReactNode
}

/** A person's names and sex, for adding someone or changing them. */
export function PersonForm({ person, problem, saving, cancelTo, onSave, renderNotice }: PersonFormProps) {
  const [names, setNames] = useState<NameRow[]>(
    person?.names.map((name) => ({ ...name, surname: name.surname ?? '' })) ?? [emptyName],
  )
  const [sex, setSex] = useState<Sex>(person?.sex ?? 'Unknown')

  const details: PersonDetails = { sex, names: names.map((name) => ({ ...name, surname: name.surname || null })) }
  const updateName = (index: number, change: Partial<NameRow>) =>
    setNames(names.map((name, i) => (i === index ? { ...name, ...change } : name)))

  const submit = (event: FormEvent) => {
    event.preventDefault()
    onSave(details)
  }

  return (
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

      {renderNotice?.(details)}

      <div className="form-buttons">
        <button type="submit" disabled={saving}>
          Save
        </button>
        <Link to={cancelTo}>Cancel</Link>
      </div>
    </form>
  )
}
