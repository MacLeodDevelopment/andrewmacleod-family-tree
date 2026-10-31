import type { ReactNode } from 'react'
import type { Person } from './people.ts'

interface DuplicateNoticeProps {
  matches: Person[]
  /** How to offer each match, which depends on what can be done with them from here. */
  renderMatch: (person: Person) => ReactNode
  adding: boolean
  onAddAnyway: () => void
}

/** The API stopped someone being added, because they may already be in the tree. */
export function DuplicateNotice({ matches, renderMatch, adding, onAddAnyway }: DuplicateNoticeProps) {
  return (
    <div role="alert" className="notice">
      <p>
        <strong>This may be a duplicate.</strong> Someone with this name already exists:
      </p>
      <ul>
        {matches.map((person) => (
          <li key={person.id}>{renderMatch(person)}</li>
        ))}
      </ul>
      <button type="button" disabled={adding} onClick={onAddAnyway}>
        It's a different person: add anyway
      </button>
    </div>
  )
}
