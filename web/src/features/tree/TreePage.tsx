import { useQuery } from '@tanstack/react-query'
import { useEffect, useMemo, useRef } from 'react'
import { Link, useParams } from 'react-router'
import { ApiError, useApi } from '../../shared/api/client.ts'
import { profilesUrl, summaryDates } from '../profile/profile.ts'
import type { Graph } from './graph.ts'
import { cardWidth, detailLineHeight, hourglassLayout, type LineStyle, type TreeCard, type TreeLayout } from './hourglassLayout.ts'

/** SVG text doesn't wrap, so long names are cut to fit, with the whole name in a tooltip. */
const maxNameLength = 22

const legend: { style: LineStyle; label: string }[] = [
  { style: 'biological', label: 'Birth parent' },
  { style: 'social', label: 'Adopted, step or foster' },
  { style: 'uncertain', label: 'Presumed or unknown' },
  { style: 'partner', label: 'Partners' },
]

/** The family around one person. Choosing anyone else re-centres it on them. */
export function TreePage() {
  const { id } = useParams()
  const api = useApi()
  const graph = useQuery({
    queryKey: ['graph', id],
    queryFn: () => api<Graph>(`${profilesUrl}/${id}/graph`),
  })

  if (graph.isPending) {
    return <p role="status">Loading…</p>
  }

  if (graph.isError) {
    const notFound = graph.error instanceof ApiError && graph.error.status === 404
    return (
      <>
        <p role="alert">{notFound ? "This person doesn't exist." : "The tree couldn't be loaded. Please try again."}</p>
        <Link to="/search">Back to search</Link>
      </>
    )
  }

  return <Tree graph={graph.data} />
}

function Tree({ graph }: { graph: Graph }) {
  const layout = useMemo(() => hourglassLayout(graph), [graph])
  const focus = graph.people.find((person) => person.id === graph.focusId)

  return (
    <>
      <p className="form-buttons">
        <Link to="/search">Back to search</Link>
        <Link to={`/family/${graph.focusId}`}>See profile</Link>
      </p>
      <h1>{focus?.name}</h1>
      <p className="people-list-dates">Choose anyone to move the tree to them.</p>

      <TreeDrawing layout={layout} focusId={graph.focusId} />

      <ul className="tree-legend" aria-label="What the lines mean">
        {legend.map(({ style, label }) => (
          <li key={style}>
            <svg width="36" height="10" aria-hidden="true">
              <path d="M 0 5 H 36" className={`tree-line tree-line--${style}`} />
            </svg>
            {label}
          </li>
        ))}
      </ul>
    </>
  )
}

function TreeDrawing({ layout, focusId }: { layout: TreeLayout; focusId: string }) {
  const scroller = useRef<HTMLDivElement>(null)
  const focus = layout.cards.find((card) => card.isFocus)

  // A wide tree scrolls, and should open with the focus person in view.
  useEffect(() => {
    if (scroller.current && focus) {
      scroller.current.scrollLeft = focus.x + cardWidth / 2 - scroller.current.clientWidth / 2
    }
  }, [focus, focusId])

  return (
    <div className="tree-scroller" ref={scroller}>
      <svg width={layout.width} height={layout.height} role="group" aria-label="Family tree">
        {layout.lines.map((line) => (
          <path key={line.key} d={line.path} className={`tree-line tree-line--${line.style}`} />
        ))}
        {layout.cards.map((card) => (
          <Card key={card.key} card={card} />
        ))}
      </svg>
    </div>
  )
}

function Card({ card }: { card: TreeCard }) {
  const { person } = card

  if (!person) {
    return (
      <g transform={`translate(${card.x} ${card.y})`}>
        <rect width={cardWidth} height={card.height} rx="8" className="tree-card tree-card--unknown" />
        <text x={cardWidth / 2} y={card.height / 2 + 5} textAnchor="middle" className="tree-card-dates">
          Unknown
        </text>
      </g>
    )
  }

  const dates = summaryDates(person)
  const name = person.name.length > maxNameLength ? `${person.name.slice(0, maxNameLength - 1)}…` : person.name
  const label = [person.name, person.isHalfSibling ? 'half-sibling' : null, dates].filter(Boolean).join(', ')

  return (
    <Link to={`/tree/${person.id}`} aria-label={label} aria-current={card.isFocus ? 'true' : undefined}>
      <g transform={`translate(${card.x} ${card.y})`}>
        <title>{person.name}</title>
        <rect width={cardWidth} height={card.height} rx="8" className={card.isFocus ? 'tree-card tree-card--focus' : 'tree-card'} />
        <text x="10" y="24" className="tree-card-name">
          {name}
        </text>
        {card.details.map((line, index) => (
          <text key={index} x="10" y={44 + index * detailLineHeight} className="tree-card-dates">
            {line}
          </text>
        ))}
      </g>
    </Link>
  )
}
