import type { QueryClient } from '@tanstack/react-query'
import type { Person } from './people.ts'

export const partnershipTypes = {
  Unknown: 'Unknown',
  Marriage: 'Marriage',
  CivilPartnership: 'Civil partnership',
  Cohabiting: 'Living together',
  None: 'Not a couple',
} as const

export const parentRelationships = ['Biological', 'Adopted', 'Step', 'Foster', 'Presumed', 'Unknown'] as const

export type PartnershipType = keyof typeof partnershipTypes
export type ParentRelationship = (typeof parentRelationships)[number]

export interface ChildLink {
  childId: string
  partner1Relationship: ParentRelationship
  partner2Relationship: ParentRelationship
}

export interface Family {
  id: string
  /** Null when this parent is unknown. */
  partner1Id: string | null
  partner2Id: string | null
  type: PartnershipType
  children: ChildLink[]
}

export interface PersonFamilies {
  asChild: Family[]
  asPartner: Family[]
  /** Everyone in those families. */
  people: Person[]
}

export const familiesUrl = '/api/v1/admin/families'

/** The other partner in a family: null if they're unknown. */
export function otherPartner(family: Family, personId: string): string | null {
  return family.partner1Id === personId ? family.partner2Id : family.partner1Id
}

/** A child's relationship to one of the family's partners. */
export function relationshipTo(family: Family, child: ChildLink, parentId: string): ParentRelationship {
  return family.partner1Id === parentId ? child.partner1Relationship : child.partner2Relationship
}

/** Fetches every family again, because a change shows on the pages of everyone involved. */
export function refreshFamilies(queryClient: QueryClient) {
  return queryClient.invalidateQueries({ queryKey: ['families'] })
}
