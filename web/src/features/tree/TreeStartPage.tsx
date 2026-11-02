import { Navigate } from 'react-router'
import { useMyLink } from '../../shared/api/myLink.ts'
import { SearchPage } from '../profile/SearchPage.tsx'

/** The tree always needs someone to centre on: you if you're linked, or whoever you choose. */
export function TreeStartPage() {
  const link = useMyLink()

  if (link.isPending) {
    return <p role="status">Loading…</p>
  }

  if (link.data?.isApproved) {
    return <Navigate to={`/tree/${link.data.personId}`} replace />
  }

  return <SearchPage title="Who should the tree start from?" linkTo={(id) => `/tree/${id}`} />
}
