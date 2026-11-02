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
  /** An adopted name has no type here: the word isn't shown to viewers. */
  names: { givenName: string; surname: string | null; type: NameType | null }[]
  events: ProfileEvent[]
  /** One row for each place in a family, so a parent nobody knows has a null person. */
  parents: { person: PersonSummary | null; relationship: ParentRelationship }[]
  siblings: { person: PersonSummary; isHalf: boolean }[]
  partnerships: ProfilePartnership[]
}

export const profilesUrl = '/api/v1/people'

/** Each of their dates on its own, such as "b. c. 1870" and "d. 1901". */
export function lifeDates(person: PersonSummary): string[] {
  return [person.born && `b. ${person.born}`, person.died && `d. ${person.died}`].filter((date) => date !== null)
}

/** Their dates in a line, such as "b. c. 1870, d. 1901". */
export function summaryDates(person: PersonSummary): string {
  return lifeDates(person).join(', ')
}
