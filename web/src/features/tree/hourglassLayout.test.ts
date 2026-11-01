import { describe, expect, it } from 'vitest'
import type { Graph, GraphChild, GraphFamily, GraphPerson } from './graph.ts'
import { cardHeight, cardWidth, detailLineHeight, hourglassLayout, type TreeCard, type TreeLayout } from './hourglassLayout.ts'

const person = (id: string): GraphPerson => ({ id, name: id, born: null, died: null, isHalfSibling: false })
const longDate = 'between 12 September 1870 and 12 September 1875'

const child = (childId: string, to1: GraphChild['partner1Relationship'] = 'Biological', to2 = to1): GraphChild => ({
  childId,
  partner1Relationship: to1,
  partner2Relationship: to2,
})

const family = (id: string, partner1Id: string | null, partner2Id: string | null, children: GraphChild[] = []): GraphFamily => ({
  id,
  partner1Id,
  partner2Id,
  type: 'Marriage',
  children,
})

function graph(focusId: string, families: GraphFamily[]): Graph {
  const ids = new Set(
    families
      .flatMap((f) => [f.partner1Id, f.partner2Id, ...f.children.map((c) => c.childId)])
      .concat(focusId)
      .filter((id) => id !== null),
  )
  return { focusId, people: [...ids].map(person), families }
}

const card = (layout: TreeLayout, key: string) => layout.cards.find((c) => c.key === key)!

function expectNoOverlaps(layout: TreeLayout) {
  const overlaps = (a: TreeCard, b: TreeCard) =>
    a !== b && Math.abs(a.x - b.x) < cardWidth && Math.abs(a.y - b.y) < cardHeight
  expect(layout.cards.filter((a) => layout.cards.some((b) => overlaps(a, b)))).toEqual([])
  for (const a of layout.cards) {
    for (const b of layout.cards.filter((other) => other.y > a.y)) {
      expect(a.y + a.height).toBeLessThan(b.y)
    }
  }
}

const remarried = graph('hub', [
  family('grandparents', 'gran', 'grandad', [child('mum'), child('aunt')]),
  family('parents', 'mum', 'dad', [child('hub'), child('sister')]),
  family('first', 'hub', 'wife1', [child('a'), child('b')]),
  family('second', 'hub', 'wife2', [child('c')]),
  family('single', 'hub', null, [child('d')]),
])

describe('The hourglass layout', () => {
  it('puts ancestors above and descendants below the focus person', () => {
    const layout = hourglassLayout(remarried)

    expect(card(layout, 'gran').y).toBeLessThan(card(layout, 'mum').y)
    expect(card(layout, 'mum').y).toBeLessThan(card(layout, 'hub').y)
    expect(card(layout, 'hub').y).toBeLessThan(card(layout, 'a').y)
  })

  it('keeps partners and brothers and sisters on the same row as each other', () => {
    const layout = hourglassLayout(remarried)

    expect(card(layout, 'wife1').y).toBe(card(layout, 'hub').y)
    expect(card(layout, 'sister').y).toBe(card(layout, 'hub').y)
    expect(card(layout, 'aunt').y).toBe(card(layout, 'mum').y)
  })

  it('stands partners next to each other, with someone remarried between them', () => {
    const layout = hourglassLayout(graph('hub', [family('first', 'hub', 'wife1'), family('second', 'hub', 'wife2')]))

    const xs = ['wife1', 'hub', 'wife2'].map((key) => card(layout, key).x)
    expect(Math.abs(xs[0] - xs[1])).toBeLessThan(cardWidth * 1.5)
    expect(Math.abs(xs[1] - xs[2])).toBeLessThan(cardWidth * 1.5)
    expect((xs[0] - xs[1]) * (xs[2] - xs[1])).toBeLessThan(0)
  })

  it('never overlaps cards, even in a complicated family', () => {
    expectNoOverlaps(hourglassLayout(remarried))
  })

  it('keeps everything inside the drawing', () => {
    const layout = hourglassLayout(remarried)

    for (const c of layout.cards) {
      expect(c.x).toBeGreaterThanOrEqual(0)
      expect(c.y).toBeGreaterThanOrEqual(0)
      expect(c.x + cardWidth).toBeLessThanOrEqual(layout.width)
      expect(c.y + c.height).toBeLessThanOrEqual(layout.height)
    }
  })

  it('gives an unknown parent a card of their own', () => {
    const layout = hourglassLayout(graph('child', [family('f', 'mum', null, [child('child', 'Biological', 'Unknown')])]))

    const unknown = layout.cards.filter((c) => c.person === null)
    expect(unknown).toHaveLength(1)
    expect(unknown[0].y).toBe(card(layout, 'mum').y)
  })

  it('gives each unknown parent their own card', () => {
    const layout = hourglassLayout(
      graph('mum', [family('f1', 'mum', null, [child('a')]), family('f2', 'mum', null, [child('b')])]),
    )

    expect(layout.cards.filter((c) => c.person === null)).toHaveLength(2)
  })

  it('has no card for an unknown partner with no children', () => {
    const layout = hourglassLayout(graph('alone', [family('f', 'alone', null)]))

    expect(layout.cards).toHaveLength(1)
  })

  it('copes with someone who has no family', () => {
    const layout = hourglassLayout({ focusId: 'alone', people: [person('alone')], families: [] })

    expect(layout.cards.map((c) => c.isFocus)).toEqual([true])
    expect(layout.lines).toEqual([])
  })

  it('marks the focus person and nobody else', () => {
    const layout = hourglassLayout(remarried)

    expect(layout.cards.filter((c) => c.isFocus).map((c) => c.key)).toEqual(['hub'])
  })

  it('draws a line to every child and between every couple', () => {
    const layout = hourglassLayout(remarried)

    expect(layout.lines.filter((line) => line.style === 'partner')).toHaveLength(4)
    expect(layout.lines.filter((line) => line.style === 'biological')).toHaveLength(8)
  })

  it.each([
    ['Biological', 'Biological', 'biological'],
    ['Biological', 'Step', 'social'],
    ['Adopted', 'Adopted', 'social'],
    ['Foster', 'Biological', 'social'],
    ['Biological', 'Presumed', 'uncertain'],
    ['Adopted', 'Unknown', 'uncertain'],
  ] as const)('shows a child %s and %s as %s', (to1, to2, style) => {
    const layout = hourglassLayout(graph('child', [family('f', 'mum', 'dad', [child('child', to1, to2)])]))

    expect(layout.lines.find((line) => line.key === 'f:child')?.style).toBe(style)
  })

  describe('with long dates', () => {
    const tree: Graph = {
      ...graph('child', [family('f', 'mum', 'dad', [child('child')])]),
    }
    tree.people = tree.people.map((p) => (p.id === 'mum' ? { ...p, born: longDate, died: longDate } : p))

    it('wraps them between words, losing nothing', () => {
      const { details } = card(hourglassLayout(tree), 'mum')

      expect(details.length).toBeGreaterThan(2)
      expect(details.join(' ')).toBe(`b. ${longDate} d. ${longDate}`)
      expect(Math.max(...details.map((line) => line.length))).toBeLessThanOrEqual(24)
    })

    it('makes the whole row tall enough for them', () => {
      const layout = hourglassLayout(tree)
      const mum = card(layout, 'mum')

      expect(mum.height).toBeGreaterThanOrEqual(44 + (mum.details.length - 1) * detailLineHeight + 6)
      expect(card(layout, 'dad').height).toBe(mum.height)
      expect(card(layout, 'child').height).toBe(cardHeight)
      expectNoOverlaps(layout)
    })

    it('starts the lines to children below the tall cards', () => {
      const layout = hourglassLayout(tree)
      const line = layout.lines.find((l) => l.key === 'f:child')!
      const mum = card(layout, 'mum')
      const bus = Number(line.path.split(' ')[4])

      expect(bus).toBeGreaterThan(mum.y + mum.height)
    })
  })

  it('ignores the relationship to an unknown parent', () => {
    const layout = hourglassLayout(graph('child', [family('f', 'mum', null, [child('child', 'Biological', 'Unknown')])]))

    expect(layout.lines.find((line) => line.key === 'f:child')?.style).toBe('biological')
  })
})
