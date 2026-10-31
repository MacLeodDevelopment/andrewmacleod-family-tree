import { useQuery } from '@tanstack/react-query'
import { Link, useParams } from 'react-router'
import { ApiError, useApi } from '../../shared/api/client.ts'
import { adminRole, useMe } from '../../shared/api/me.ts'
import { partnershipTypes, type ParentRelationship } from '../people/families.ts'
import { profilesUrl, summaryDates, type PersonSummary, type Profile, type ProfileEvent, type ProfilePartnership } from './profile.ts'

/** Someone's life and closest family, read-only, for every family member. */
export function ProfilePage() {
  const { id } = useParams()
  const api = useApi()
  const profile = useQuery({
    queryKey: ['profile', id],
    queryFn: () => api<Profile>(`${profilesUrl}/${id}`),
  })

  if (profile.isPending) {
    return <p role="status">Loading…</p>
  }

  if (profile.isError) {
    const notFound = profile.error instanceof ApiError && profile.error.status === 404
    return (
      <>
        <p role="alert">{notFound ? "This person doesn't exist." : "This person couldn't be loaded. Please try again."}</p>
        <Link to="/search">Back to search</Link>
      </>
    )
  }

  return <ProfileDetails profile={profile.data} />
}

function ProfileDetails({ profile }: { profile: Profile }) {
  const me = useMe()
  const [main, ...otherNames] = profile.names
  const { isPrivate, parents, siblings, partnerships, events } = profile
  const nothingKnown = parents.length === 0 && siblings.length === 0 && partnerships.length === 0

  return (
    <>
      <p>
        <Link to="/search">Back to search</Link>
      </p>
      <div className="page-heading">
        <h1>{nameText(main)}</h1>
        {me.data?.roles.includes(adminRole) && (
          <Link className="button" to={`/people/${profile.id}`}>
            Edit
          </Link>
        )}
      </div>

      {otherNames.length > 0 && (
        <p className="people-list-dates">
          Also known as {otherNames.map((name) => `${nameText(name)} (${name.type.toLowerCase()})`).join(', ')}
        </p>
      )}

      <h2>Life</h2>
      {isPrivate && <p>Dates and places are hidden, because this person may be living.</p>}
      {!isPrivate && (events.length === 0 ? <p>No events recorded yet.</p> : <Events events={events} />)}

      <h2>Family</h2>
      {nothingKnown && <p>No family recorded yet.</p>}

      {parents.length > 0 && (
        <>
          <h3>Parents</h3>
          <ul className="relatives">
            {parents.map(({ person, relationship }, index) => (
              <li key={index}>{person ? <Relative person={person} relationship={relationship} /> : 'Unknown'}</li>
            ))}
          </ul>
        </>
      )}

      {siblings.length > 0 && (
        <>
          <h3>Brothers and sisters</h3>
          <ul className="relatives">
            {siblings.map(({ person, isHalf }) => (
              <li key={person.id}>
                <Relative person={person} note={isHalf ? 'half' : undefined} />
              </li>
            ))}
          </ul>
        </>
      )}

      {partnerships.map((partnership, index) => (
        <Partnership key={index} partnership={partnership} />
      ))}
    </>
  )
}

function Partnership({ partnership }: { partnership: ProfilePartnership }) {
  const { partner, type, events, children } = partnership

  return (
    <>
      <h3>
        {partner ? (
          <>
            With <Link to={`/family/${partner.id}`}>{partner.name}</Link>
          </>
        ) : (
          'With someone unknown'
        )}
        {type !== 'Unknown' && type !== 'None' && ` · ${partnershipTypes[type]}`}
      </h3>

      {events.length > 0 && <Events events={events} />}

      {children.length > 0 && (
        <ul className="relatives">
          {children.map(({ person, relationship }) => (
            <li key={person.id}>
              <Relative person={person} relationship={relationship} />
            </li>
          ))}
        </ul>
      )}
    </>
  )
}

function Events({ events }: { events: ProfileEvent[] }) {
  return (
    <ul className="timeline">
      {events.map((event) => (
        <li key={event.type}>
          <strong>{event.type}</strong>
          {[event.date, event.place].filter(Boolean).map((detail) => ` · ${detail}`)}
        </li>
      ))}
    </ul>
  )
}

interface RelativeProps {
  person: PersonSummary
  relationship?: ParentRelationship
  note?: string
}

function Relative({ person, relationship, note }: RelativeProps) {
  const label = note ?? (relationship && relationship !== 'Biological' ? relationship.toLowerCase() : null)

  return (
    <>
      <Link to={`/family/${person.id}`}>{person.name}</Link>
      {label && ` (${label})`}
      <span className="people-list-dates"> {summaryDates(person)}</span>
    </>
  )
}

function nameText(name: Profile['names'][number]): string {
  return [name.givenName, name.surname].filter(Boolean).join(' ')
}
