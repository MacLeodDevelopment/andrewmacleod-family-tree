import { useMutation, useQueryClient } from '@tanstack/react-query'
import { useState, type FormEvent } from 'react'
import { ApiError, useApi } from '../../shared/api/client.ts'
import {
  familiesUrl,
  otherPartner,
  parentRelationships,
  partnershipTypes,
  refreshFamilies,
  type Family,
  type ParentRelationship,
  type PartnershipType,
} from './families.ts'
import { PersonPicker } from './PersonPicker.tsx'
import { fullName, type Person } from './people.ts'

/** Why the API refused, which is usually a rule such as nobody being their own ancestor. */
function SaveError({ error }: { error: Error | null }) {
  if (!error) {
    return null
  }

  const reasons = error instanceof ApiError ? Object.values(error.problem.errors ?? {}).flat() : []
  return (
    <p role="alert" className="field-error">
      {reasons.length > 0 ? reasons.join(' ') : "This couldn't be saved. Please try again."}
    </p>
  )
}

function RelationshipSelect({
  label,
  value,
  onChange,
}: {
  label: string
  value: ParentRelationship
  onChange: (value: ParentRelationship) => void
}) {
  return (
    <label>
      {label}
      <select value={value} onChange={(event) => onChange(event.target.value as ParentRelationship)}>
        {parentRelationships.map((relationship) => (
          <option key={relationship}>{relationship}</option>
        ))}
      </select>
    </label>
  )
}

interface AddRelativeProps {
  person: Person
  onDone: () => void
}

export function AddPartnerForm({ person, onDone }: AddRelativeProps) {
  const api = useApi()
  const queryClient = useQueryClient()
  const [partnerId, setPartnerId] = useState<string | null>(null)
  const [type, setType] = useState<PartnershipType>('Marriage')

  const save = useMutation({
    mutationFn: () =>
      api<Family>(familiesUrl, {
        method: 'POST',
        body: JSON.stringify({ partner1Id: person.id, partner2Id: partnerId, type }),
      }),
    onSuccess: async () => {
      await refreshFamilies(queryClient)
      onDone()
    },
  })

  const submit = (event: FormEvent) => {
    event.preventDefault()
    save.mutate()
  }

  return (
    <form className="form" aria-label="Add a partner" onSubmit={submit} noValidate>
      <h3>Add a partner</h3>
      <PersonPicker excludeId={person.id} chosenId={partnerId} onChoose={setPartnerId} />

      <label>
        Type of partnership
        <select value={type} onChange={(event) => setType(event.target.value as PartnershipType)}>
          {Object.entries(partnershipTypes).map(([value, label]) => (
            <option key={value} value={value}>
              {label}
            </option>
          ))}
        </select>
      </label>

      <SaveError error={save.error} />

      <div className="form-buttons">
        <button type="submit" disabled={!partnerId || save.isPending}>
          Save
        </button>
        <button type="button" onClick={onDone}>
          Cancel
        </button>
      </div>
    </form>
  )
}

/** A new family, for a child whose other parent isn't known and who has no such family yet. */
const newFamily = 'new'

export function AddChildForm({
  person,
  families,
  people,
  onDone,
}: AddRelativeProps & { families: Family[]; people: Person[] }) {
  const api = useApi()
  const queryClient = useQueryClient()
  const [childId, setChildId] = useState<string | null>(null)
  const [familyId, setFamilyId] = useState(families[0]?.id ?? newFamily)
  const [toPerson, setToPerson] = useState<ParentRelationship>('Biological')
  const [toOther, setToOther] = useState<ParentRelationship>('Biological')

  const partnerName = (family: Family) => {
    const partner = people.find((candidate) => candidate.id === otherPartner(family, person.id))
    return partner ? fullName(partner) : null
  }
  const family = families.find((candidate) => candidate.id === familyId)
  const otherName = family ? partnerName(family) : null

  const save = useMutation({
    mutationFn: () => {
      if (!family) {
        const child = { childId, partner1Relationship: toPerson }
        return api<Family>(familiesUrl, {
          method: 'POST',
          body: JSON.stringify({ partner1Id: person.id, type: 'None', children: [child] }),
        })
      }

      const [partner1Relationship, partner2Relationship] =
        family.partner1Id === person.id ? [toPerson, toOther] : [toOther, toPerson]
      return api<Family>(`${familiesUrl}/${family.id}/children`, {
        method: 'POST',
        body: JSON.stringify({ childId, partner1Relationship, partner2Relationship }),
      })
    },
    onSuccess: async () => {
      await refreshFamilies(queryClient)
      onDone()
    },
  })

  const submit = (event: FormEvent) => {
    event.preventDefault()
    save.mutate()
  }

  const hasUnknownParentFamily = families.some((candidate) => otherPartner(candidate, person.id) === null)

  return (
    <form className="form" aria-label="Add a child" onSubmit={submit} noValidate>
      <h3>Add a child</h3>
      <PersonPicker excludeId={person.id} chosenId={childId} onChoose={setChildId} />

      <label>
        Other parent
        <select value={familyId} onChange={(event) => setFamilyId(event.target.value)}>
          {families.map((candidate) => (
            <option key={candidate.id} value={candidate.id}>
              {partnerName(candidate) ?? 'Unknown'}
            </option>
          ))}
          {!hasUnknownParentFamily && <option value={newFamily}>Unknown</option>}
        </select>
      </label>

      <RelationshipSelect label={`Relationship to ${fullName(person)}`} value={toPerson} onChange={setToPerson} />
      {otherName && <RelationshipSelect label={`Relationship to ${otherName}`} value={toOther} onChange={setToOther} />}

      <SaveError error={save.error} />

      <div className="form-buttons">
        <button type="submit" disabled={!childId || save.isPending}>
          Save
        </button>
        <button type="button" onClick={onDone}>
          Cancel
        </button>
      </div>
    </form>
  )
}
