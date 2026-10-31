export const nameTypes = ['Birth', 'Married', 'Alias', 'Nickname'] as const
export const sexes = ['Unknown', 'Female', 'Male'] as const
export const eventTypes = ['Birth', 'Death'] as const

export type NameType = (typeof nameTypes)[number]
export type Sex = (typeof sexes)[number]
export type EventType = (typeof eventTypes)[number]

export interface PersonName {
  givenName: string
  surname: string | null
  type: NameType
}

export interface LifeEvent {
  id: string
  type: EventType
  /** The date for reading, such as "c. 1870". */
  date: string | null
  /** The date as it was typed, such as "abt 1870". */
  dateAsEntered: string | null
  place: string | null
}

export interface Person {
  id: string
  sex: Sex
  names: PersonName[]
  events: LifeEvent[]
}

export interface PeoplePage {
  items: Person[]
  total: number
}

export const peopleUrl = '/api/v1/admin/people'

/** The person's main name, which is the first in their list. */
export function fullName(person: Person): string {
  const [name] = person.names
  return [name?.givenName, name?.surname].filter(Boolean).join(' ')
}

/** Their dates in a line, such as "b. c. 1870, d. 1901". */
export function lifeDates(person: Person): string {
  const date = (type: EventType, label: string) => {
    const found = person.events.find((event) => event.type === type)?.date
    return found ? `${label} ${found}` : null
  }

  return [date('Birth', 'b.'), date('Death', 'd.')].filter(Boolean).join(', ')
}
