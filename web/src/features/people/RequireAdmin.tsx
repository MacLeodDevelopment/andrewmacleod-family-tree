import type { ReactNode } from 'react'
import { ApiError } from '../../shared/api/client.ts'
import { adminRole, useMe } from '../../shared/api/me.ts'
import { useAuth } from '../../shared/auth/authContext.ts'

/**
 * Shows admin pages to admins. This only keeps the screens tidy: the real protection is
 * the API, which refuses these requests from anyone else whatever the browser does.
 */
export function RequireAdmin({ children }: { children: ReactNode }) {
  const auth = useAuth()
  const me = useMe()

  if (!auth.user) {
    return <p>Please sign in to see this page.</p>
  }

  if (me.isPending) {
    return <p role="status">Loading…</p>
  }

  if (me.isError && !(me.error instanceof ApiError && me.error.status === 403)) {
    return <p role="alert">The family tree couldn't be reached. Please try again later.</p>
  }

  return me.data?.roles.includes(adminRole) ? children : <p role="alert">You don't have access to this page.</p>
}
