import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useState, type FormEvent } from 'react'
import { Link } from 'react-router'
import { useApi } from '../../shared/api/client.ts'
import { AddChildForm, AddParentForm, AddPartnerForm, AddSiblingForm, SaveError } from './AddRelative.tsx'
import { ConfirmRemove } from './ConfirmRemove.tsx'
import { EventsEditor } from './EventsEditor.tsx'
import {
  familiesUrl,
  familyEventTypes,
  otherPartner,
  parentsOf,
  partnershipTypes,
  personName,
  refreshFamilies,
  relationshipTo,
  type Family,
  type ParentRelationship,
  type PartnershipType,
  type PersonFamilies,
} from './families.ts'
import { fullName, lifeDates, peopleUrl, type Person } from './people.ts'

function Relative({ id, people, relationship }: { id: string; people: Person[]; relationship?: ParentRelationship }) {
  const person = people.find((candidate) => candidate.id === id)
  const showRelationship = relationship && relationship !== 'Biological'

  return (
    <span>
      <Link to={`/people/${id}`}>{personName(id, people)}</Link>
      {showRelationship && ` (${relationship.toLowerCase()})`}
      {person && <span className="people-list-dates"> {lifeDates(person)}</span>}
    </span>
  )
}

type Adding = 'parent' | 'sibling' | 'partner' | 'child'

/** Everyone a person is directly related to, and how to change that. */
export function Relatives({ person }: { person: Person }) {
  const api = useApi()
  const [adding, setAdding] = useState<Adding | null>(null)
  const families = useQuery({
    queryKey: ['families', person.id],
    queryFn: () => api<PersonFamilies>(`${peopleUrl}/${person.id}/families`),
  })

  if (families.isPending) {
    return null
  }

  if (families.isError) {
    return <p role="alert">This person's family couldn't be loaded. Please try again.</p>
  }

  const { asChild, asPartner, people } = families.data
  const siblings = [
    ...new Set(asChild.flatMap((family) => family.children.map((child) => child.childId)).filter((id) => id !== person.id)),
  ]
  const nothingRecorded = asChild.length === 0 && asPartner.length === 0
  const onDone = () => setAdding(null)

  return (
    <>
      <h2>Family</h2>
      {nothingRecorded && <p>No family recorded yet.</p>}

      {asChild.length > 0 && <h3>Parents</h3>}
      {asChild.map((family) => (
        <Parents key={family.id} family={family} person={person} people={people} />
      ))}

      {siblings.length > 0 && (
        <>
          <h3>Brothers and sisters</h3>
          <ul className="relatives">
            {siblings.map((id) => (
              <li key={id}>
                <Relative id={id} people={people} />
              </li>
            ))}
          </ul>
        </>
      )}

      {asPartner.map((family) => (
        <Partnership key={family.id} family={family} person={person} people={people} />
      ))}

      {adding === null && (
        <div className="form-buttons relatives-buttons">
          <button type="button" onClick={() => setAdding('parent')}>
            Add a parent
          </button>
          {asChild.length > 0 && (
            <button type="button" onClick={() => setAdding('sibling')}>
              Add a brother or sister
            </button>
          )}
          <button type="button" onClick={() => setAdding('partner')}>
            Add a partner
          </button>
          <button type="button" onClick={() => setAdding('child')}>
            Add a child
          </button>
        </div>
      )}
      {adding === 'parent' && <AddParentForm person={person} families={asChild} people={people} onDone={onDone} />}
      {adding === 'sibling' && <AddSiblingForm person={person} families={asChild} people={people} onDone={onDone} />}
      {adding === 'partner' && <AddPartnerForm person={person} onDone={onDone} />}
      {adding === 'child' && <AddChildForm person={person} families={asPartner} people={people} onDone={onDone} />}
    </>
  )
}

function Parents({ family, person, people }: { family: Family; person: Person; people: Person[] }) {
  const api = useApi()
  const link = family.children.find((child) => child.childId === person.id)!
  const parents = [
    { id: family.partner1Id, relationship: link.partner1Relationship },
    { id: family.partner2Id, relationship: link.partner2Relationship },
  ]
  const names = parentsOf(family, people)

  return (
    <>
      <ul className="relatives">
        {parents.map(({ id, relationship }, index) => (
          <li key={index}>{id ? <Relative id={id} people={people} relationship={relationship} /> : 'Unknown'}</li>
        ))}
      </ul>
      <ConfirmRemove
        label="Remove from these parents"
        buttonName={`Remove from these parents: ${names}`}
        question={`Remove ${fullName(person)} from the family of ${names}? The people stay in the tree.`}
        remove={() => api<void>(`${familiesUrl}/${family.id}/children/${person.id}`, { method: 'DELETE' })}
      />
    </>
  )
}

function Partnership({ family, person, people }: { family: Family; person: Person; people: Person[] }) {
  const api = useApi()
  const queryClient = useQueryClient()
  const [changingType, setChangingType] = useState(false)
  const partnerId = otherPartner(family, person.id)
  const partner = partnerId ? personName(partnerId, people) : 'someone unknown'
  const hasChildren = family.children.length > 0

  return (
    <>
      <h3>
        {partnerId ? (
          <>
            With <Link to={`/people/${partnerId}`}>{partner}</Link>
          </>
        ) : (
          'With someone unknown'
        )}
        {family.type !== 'Unknown' && family.type !== 'None' && ` · ${partnershipTypes[family.type]}`}
      </h3>

      <EventsEditor
        events={family.events}
        types={familyEventTypes}
        url={`${familiesUrl}/${family.id}/events`}
        refresh={() => refreshFamilies(queryClient)}
        addLabel="Add a marriage or divorce"
        labelSuffix={` with ${partner}`}
      />

      {hasChildren && (
        <ul className="relatives">
          {family.children.map((child) => (
            <li key={child.childId} className="relatives-row">
              <Relative id={child.childId} people={people} relationship={relationshipTo(family, child, person.id)} />
              <ConfirmRemove
                buttonName={`Remove ${personName(child.childId, people)} from this family`}
                question={`Remove ${personName(child.childId, people)} as a child of this family? The people stay in the tree.`}
                remove={() => api<void>(`${familiesUrl}/${family.id}/children/${child.childId}`, { method: 'DELETE' })}
              />
            </li>
          ))}
        </ul>
      )}

      {changingType ? (
        <ChangeTypeForm family={family} partner={partner} onDone={() => setChangingType(false)} />
      ) : (
        <div className="form-buttons relatives-row-buttons">
          <button type="button" className="small" aria-label={`Change partnership with ${partner}`} onClick={() => setChangingType(true)}>
            Change partnership
          </button>
          {partnerId && (
            <ConfirmRemove
              label="Remove partnership"
              buttonName={`Remove partnership with ${partner}`}
              question={
                hasChildren
                  ? `Remove ${partner} from this family? The children stay with ${fullName(person)}.`
                  : `Remove the partnership with ${partner}? Both people stay in the tree.`
              }
              remove={() => api<void>(`${familiesUrl}/${family.id}/partners/${partnerId}`, { method: 'DELETE' })}
            />
          )}
          {!partnerId && !hasChildren && (
            <ConfirmRemove
              label="Remove partnership"
              buttonName="Remove partnership with someone unknown"
              question="Remove this empty family?"
              remove={() => api<void>(`${familiesUrl}/${family.id}`, { method: 'DELETE' })}
            />
          )}
        </div>
      )}
    </>
  )
}

function ChangeTypeForm({ family, partner, onDone }: { family: Family; partner: string; onDone: () => void }) {
  const api = useApi()
  const queryClient = useQueryClient()
  const [type, setType] = useState(family.type)

  const save = useMutation({
    mutationFn: () => api<Family>(`${familiesUrl}/${family.id}`, { method: 'PUT', body: JSON.stringify({ type }) }),
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
    <form className="form" aria-label={`Change partnership with ${partner}`} onSubmit={submit} noValidate>
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
        <button type="submit" disabled={save.isPending}>
          Save
        </button>
        <button type="button" onClick={onDone}>
          Cancel
        </button>
      </div>
    </form>
  )
}
