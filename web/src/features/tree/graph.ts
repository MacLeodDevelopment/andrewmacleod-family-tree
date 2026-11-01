import type { ParentRelationship, PartnershipType } from '../people/families.ts'

/** The tree around someone, as the API gives it: who is related to whom, with no positions. */
export interface GraphPerson {
  id: string
  name: string
  born: string | null
  died: string | null
  isHalfSibling: boolean
}

export interface GraphChild {
  childId: string
  partner1Relationship: ParentRelationship
  partner2Relationship: ParentRelationship
}

export interface GraphFamily {
  id: string
  /** Null when this parent is unknown. */
  partner1Id: string | null
  partner2Id: string | null
  type: PartnershipType
  children: GraphChild[]
}

export interface Graph {
  focusId: string
  people: GraphPerson[]
  families: GraphFamily[]
}
