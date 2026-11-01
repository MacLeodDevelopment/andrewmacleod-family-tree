import type { Graph, GraphChild, GraphFamily, GraphPerson } from './graph.ts'

export const cardWidth = 170
export const cardHeight = 58
const cardGap = 20
const groupGap = 44
const rowGap = 76
const margin = 16
const busSpacing = 8
const sweeps = 6

/** How sure a line between parent and child is, drawn differently so it can be seen at a glance. */
export type LineStyle = 'partner' | 'biological' | 'social' | 'uncertain'

export interface TreeCard {
  key: string
  /** Null for a parent nobody knows. */
  person: GraphPerson | null
  isFocus: boolean
  x: number
  y: number
}

export interface TreeLine {
  key: string
  path: string
  style: LineStyle
}

export interface TreeLayout {
  cards: TreeCard[]
  lines: TreeLine[]
  width: number
  height: number
}

/** A family with its unknown partner filled in by a placeholder, so everyone has a key to place. */
interface Unit {
  family: GraphFamily
  partners: string[]
}

/** People who stand side by side: partners, kept together however the row is ordered. */
interface Group {
  keys: string[]
  x: number
}

const unknownKey = (family: GraphFamily) => `unknown:${family.id}`

/**
 * Lays a graph out as an hourglass: the focus person's row in the middle, ancestors above,
 * descendants below, and partners side by side. It only places things: it knows nothing
 * of how they're drawn, so a different renderer could use it.
 */
export function hourglassLayout(graph: Graph): TreeLayout {
  const units = graph.families.map(toUnit)
  const generations = assignGenerations(graph, units)
  const rows = buildRows(generations, units)
  const positions = placeCards(rows, units)

  const firstGeneration = Math.min(...generations.values())
  const lowest = Math.min(...positions.values())
  const x = (key: string) => positions.get(key)! - lowest + margin
  const y = (key: string) => (generations.get(key)! - firstGeneration) * (cardHeight + rowGap) + margin

  const people = new Map(graph.people.map((person) => [person.id, person]))
  const cards = [...generations.keys()].map((key) => ({
    key,
    person: people.get(key) ?? null,
    isFocus: key === graph.focusId,
    x: x(key),
    y: y(key),
  }))

  return {
    cards,
    lines: buildLines(units, x, y),
    width: Math.max(...cards.map((card) => card.x)) + cardWidth + margin,
    height: Math.max(...cards.map((card) => card.y)) + cardHeight + margin,
  }
}

function toUnit(family: GraphFamily): Unit {
  const known = [family.partner1Id, family.partner2Id].filter((id) => id !== null)
  const needsPlaceholder = known.length < 2 && family.children.length > 0

  return { family, partners: needsPlaceholder ? [...known, unknownKey(family)] : known }
}

/** Walks out from the focus person: parents are a generation up, children one down, partners level. */
function assignGenerations(graph: Graph, units: Unit[]): Map<string, number> {
  const generations = new Map<string, number>([[graph.focusId, 0]])
  const queue = [graph.focusId]

  const visit = (key: string, generation: number) => {
    if (!generations.has(key)) {
      generations.set(key, generation)
      queue.push(key)
    }
  }

  for (let next = queue.shift(); next !== undefined; next = queue.shift()) {
    const generation = generations.get(next)!

    for (const { family, partners } of units) {
      const children = family.children.map((child) => child.childId)
      if (children.includes(next)) {
        partners.forEach((partner) => visit(partner, generation - 1))
        children.forEach((child) => visit(child, generation))
      }
      if (partners.includes(next)) {
        partners.forEach((partner) => visit(partner, generation))
        children.forEach((child) => visit(child, generation + 1))
      }
    }
  }

  return generations
}

/** Splits each generation into groups of partners, in the order they were reached. */
function buildRows(generations: Map<string, number>, units: Unit[]): Group[][] {
  const groupOf = new Map<string, string[]>()

  for (const key of generations.keys()) {
    groupOf.set(key, [key])
  }

  for (const { partners } of units) {
    for (const partner of partners.slice(1)) {
      const first = groupOf.get(partners[0])
      const second = groupOf.get(partner)
      if (first && second && first !== second && generations.get(partners[0]) === generations.get(partner)) {
        first.push(...second)
        second.forEach((key) => groupOf.set(key, first))
      }
    }
  }

  const byGeneration = new Map<number, Group[]>()
  const seen = new Set<string[]>()

  for (const [key, generation] of [...generations].sort(([, a], [, b]) => a - b)) {
    const keys = groupOf.get(key)!
    if (!seen.has(keys)) {
      seen.add(keys)
      const row = byGeneration.get(generation) ?? []
      row.push({ keys: withHubInMiddle(keys, units), x: 0 })
      byGeneration.set(generation, row)
    }
  }

  return [...byGeneration.entries()].sort(([a], [b]) => a - b).map(([, row]) => row)
}

/** Someone with several partners stands between them, so each couple is next to each other. */
function withHubInMiddle(keys: string[], units: Unit[]): string[] {
  const partnerCount = (key: string) => units.filter(({ partners }) => partners.includes(key)).length
  const hub = keys.reduce((best, key) => (partnerCount(key) > partnerCount(best) ? key : best))
  const others = keys.filter((key) => key !== hub)
  const middle = Math.floor(others.length / 2)

  return [...others.slice(0, middle), hub, ...others.slice(middle)]
}

const groupWidth = (group: Group) => group.keys.length * (cardWidth + cardGap) - cardGap

/**
 * Lays each row out, then repeatedly moves each group towards the people it's joined to
 * above and below. The positions don't have to be perfect, only readable: lines may
 * cross in a complicated family, but cards never overlap.
 */
function placeCards(rows: Group[][], units: Unit[]): Map<string, number> {
  const positions = new Map<string, number>()

  const store = (row: Group[]) =>
    row.forEach((group) =>
      group.keys.forEach((key, index) => positions.set(key, group.x + index * (cardWidth + cardGap))),
    )

  const spread = (row: Group[]) => {
    let edge = -Infinity
    for (const group of row) {
      group.x = Math.max(group.x, edge)
      edge = group.x + groupWidth(group) + groupGap
    }
    store(row)
  }

  rows.forEach(spread)

  const connected = (key: string) =>
    units.flatMap(({ family, partners }) => {
      const children = family.children.map((child) => child.childId)
      if (partners.includes(key)) {
        return children
      }
      return children.includes(key) ? partners : []
    })

  const wanted = (group: Group) => {
    const xs = group.keys
      .flatMap(connected)
      .map((key) => positions.get(key))
      .filter((x) => x !== undefined)

    return xs.length === 0 ? group.x : xs.reduce((sum, x) => sum + x, 0) / xs.length + cardWidth / 2 - groupWidth(group) / 2
  }

  for (let sweep = 0; sweep < sweeps; sweep++) {
    const order = sweep % 2 === 0 ? rows : [...rows].reverse()
    for (const row of order) {
      row.forEach((group) => (group.x = wanted(group)))
      row.sort((a, b) => a.x - b.x)
      spread(row)
    }
  }

  return positions
}

/** A child's line shows the least certain link to the parents who are known. */
function lineStyle(family: GraphFamily, child: GraphChild): LineStyle {
  const relationships = [
    family.partner1Id !== null ? child.partner1Relationship : null,
    family.partner2Id !== null ? child.partner2Relationship : null,
  ].filter((relationship) => relationship !== null)

  if (relationships.some((relationship) => relationship === 'Presumed' || relationship === 'Unknown')) {
    return 'uncertain'
  }

  return relationships.every((relationship) => relationship === 'Biological') ? 'biological' : 'social'
}

function buildLines(units: Unit[], x: (key: string) => number, y: (key: string) => number): TreeLine[] {
  const lines: TreeLine[] = []
  const busesInRow = new Map<number, number>()

  for (const { family, partners } of units) {
    if (partners.length === 0) {
      continue
    }

    const centres = partners.map((partner) => x(partner) + cardWidth / 2)
    const junction = centres.reduce((sum, centre) => sum + centre, 0) / centres.length
    const middleY = y(partners[0]) + cardHeight / 2

    if (partners.length === 2 && family.type !== 'None') {
      lines.push({
        key: `${family.id}:partners`,
        path: `M ${centres[0]} ${middleY} H ${centres[1]}`,
        style: family.partner1Id !== null && family.partner2Id !== null ? 'partner' : 'uncertain',
      })
    }

    const bottom = y(partners[0]) + cardHeight
    const bus = busesInRow.get(bottom) ?? 0
    busesInRow.set(bottom, bus + 1)
    const busY = bottom + rowGap / 2 - busSpacing * (bus % 4)

    for (const child of family.children) {
      lines.push({
        key: `${family.id}:${child.childId}`,
        path: `M ${junction} ${middleY} V ${busY} H ${x(child.childId) + cardWidth / 2} V ${y(child.childId)}`,
        style: lineStyle(family, child),
      })
    }
  }

  return lines
}
