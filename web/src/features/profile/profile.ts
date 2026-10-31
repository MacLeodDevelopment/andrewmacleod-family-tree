import type { ParentRelationship, PartnershipType } from '../people/families.ts'
import type { NameType } from '../people/people.ts'

/** Enough to recognise someone and tell them from others with the same name. */
export interface PersonSummary {
  id: string
  name: string
  born: string | null
  died: string | null
}

export interface SearchResults {
  items: PersonSummary[]
  total: number
}

export interface ProfileEvent {
  type: string
  date: string | null
  place: string | null
}

export interface ProfilePartnership {
  /** Null when they're unknown. */
  partner: PersonSummary | null
  type: PartnershipType
  events: ProfileEvent[]
  children: { person: PersonSummary; relationship: ParentRelationship }[]
}

export interface Profile {
  id: string
  /** Their details are hidden because they may be living. */
  isPrivate: boolean
  names: { givenName: string; surname: string | null; type: NameType }[]
  events: ProfileEvent[]
  /** One row for each place in a family, so a parent nobody knows has a null person. */
  parents: { person: PersonSummary | null; relationship: ParentRelationship }[]
  siblings: { person: PersonSummary; isHalf: boolean }[]
  partnerships: ProfilePartnership[]
}

export const profilesUrl = '/api/v1/people'

/** Their dates in a line, such as "b. c. 1870, d. 1901". */
export function summaryDates(person: PersonSummary): string {
  return [person.born && `b. ${person.born}`, person.died && `d. ${person.died}`].filter(Boolean).join(', ')
}
