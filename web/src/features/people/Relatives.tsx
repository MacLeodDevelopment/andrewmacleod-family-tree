import { useQuery } from '@tanstack/react-query'
import { useState } from 'react'
import { Link } from 'react-router'
import { useApi } from '../../shared/api/client.ts'
import { AddChildForm, AddPartnerForm } from './AddRelative.tsx'
import {
  otherPartner,
  partnershipTypes,
  relationshipTo,
  type Family,
  type ParentRelationship,
  type PersonFamilies,
} from './families.ts'
import { fullName, lifeDates, peopleUrl, type Person } from './people.ts'

function Relative({ id, people, relationship }: { id: string; people: Person[]; relationship?: ParentRelationship }) {
  const person = people.find((candidate) => candidate.id === id)
  const showRelationship = relationship && relationship !== 'Biological'

  return (
    <>
      <Link to={`/people/${id}`}>{person ? fullName(person) : 'Unnamed'}</Link>
      {showRelationship && ` (${relationship.toLowerCase()})`}
      {person && <span className="people-list-dates"> {lifeDates(person)}</span>}
    </>
  )
}

/** Everyone a person is directly related to, and how to add to them. */
export function Relatives({ person }: { person: Person }) {
  const api = useApi()
  const [adding, setAdding] = useState<'partner' | 'child' | null>(null)
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

  return (
    <>
      <h2>Family</h2>
      {nothingRecorded && <p>No family recorded yet.</p>}

      {asChild.length > 0 && (
        <>
          <h3>Parents</h3>
          <ul className="relatives">{asChild.map((family) => <Parents key={family.id} family={family} person={person} people={people} />)}</ul>
        </>
      )}

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
          <button type="button" onClick={() => setAdding('partner')}>
            Add a partner
          </button>
          <button type="button" onClick={() => setAdding('child')}>
            Add a child
          </button>
        </div>
      )}
      {adding === 'partner' && <AddPartnerForm person={person} onDone={() => setAdding(null)} />}
      {adding === 'child' && (
        <AddChildForm person={person} families={asPartner} people={people} onDone={() => setAdding(null)} />
      )}
    </>
  )
}

function Parents({ family, person, people }: { family: Family; person: Person; people: Person[] }) {
  const link = family.children.find((child) => child.childId === person.id)!
  const parents = [
    { id: family.partner1Id, relationship: link.partner1Relationship },
    { id: family.partner2Id, relationship: link.partner2Relationship },
  ]

  return parents.map(({ id, relationship }, index) => (
    <li key={index}>{id ? <Relative id={id} people={people} relationship={relationship} /> : 'Unknown'}</li>
  ))
}

function Partnership({ family, person, people }: { family: Family; person: Person; people: Person[] }) {
  const partnerId = otherPartner(family, person.id)
  const partner = people.find((candidate) => candidate.id === partnerId)
  const type = partnershipTypes[family.type]

  return (
    <>
      <h3>
        {partnerId ? (
          <>
            With <Link to={`/people/${partnerId}`}>{partner ? fullName(partner) : 'Unnamed'}</Link>
          </>
        ) : (
          'With someone unknown'
        )}
        {family.type !== 'Unknown' && family.type !== 'None' && ` · ${type}`}
      </h3>
      {family.children.length > 0 && (
        <ul className="relatives">
          {family.children.map((child) => (
            <li key={child.childId}>
              <Relative id={child.childId} people={people} relationship={relationshipTo(family, child, person.id)} />
            </li>
          ))}
        </ul>
      )}
    </>
  )
}
