import { useQueryClient } from '@tanstack/react-query'
import { EventsEditor } from './EventsEditor.tsx'
import { eventTypes, peopleUrl, refreshPerson, type Person } from './people.ts'

/** A person's timeline, and everything that changes it. */
export function LifeEvents({ person }: { person: Person }) {
  const queryClient = useQueryClient()

  return (
    <>
      <h2>Life events</h2>
      <EventsEditor
        events={person.events}
        types={eventTypes}
        url={`${peopleUrl}/${person.id}/events`}
        refresh={() => refreshPerson(queryClient, person.id)}
        emptyText="No events recorded yet."
      />
    </>
  )
}
