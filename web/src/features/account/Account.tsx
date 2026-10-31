import { useEffect, useState } from 'react'
import { ApiError } from '../../shared/api/client.ts'
import { useMe, type Me } from '../../shared/api/me.ts'
import type { SignedInUser } from '../../shared/auth/auth.ts'
import { useAuth } from '../../shared/auth/authContext.ts'

const roleNames: Record<string, string> = {
  'Family.Admin': 'Family admin',
  'Family.Viewer': 'Family viewer',
}

const problemLabels = {
  noAccess: 'No access',
  failed: 'Unavailable',
}

/** Sizes the role badge, so it's the same width whatever it's showing. */
const widestBadgeLabel = [...Object.values(roleNames), ...Object.values(problemLabels)].reduce((widest, label) =>
  label.length > widest.length ? label : widest,
)

type MeState =
  | { kind: 'loading' }
  | { kind: 'loaded'; me: Me }
  | { kind: 'noAccess' }
  | { kind: 'failed' }

/** Sign in and out; once signed in, who you are and your role (confirmed by the API). */
export function Account() {
  const auth = useAuth()

  if (!auth.isConfigured) {
    return (
      <div className="account">
        <p className="account-message">Sign-in isn't set up in this copy of the app.</p>
      </div>
    )
  }

  if (!auth.user) {
    return (
      <div className="account">
        {auth.signInError && (
          <p role="alert" className="account-error">
            {auth.signInError}
          </p>
        )}
        <button type="button" onClick={auth.signIn}>
          Sign in
        </button>
      </div>
    )
  }

  return <SignedIn user={auth.user} />
}

/**
 * How long to wait before showing the spinner in the role badge. Most checks finish
 * within this, and a spinner that flashes up and vanishes looks like a glitch, so on
 * a fast connection the empty badge is simply filled in.
 */
export const checkingIndicatorDelayMs = 500

function SignedIn({ user }: { user: SignedInUser }) {
  const auth = useAuth()
  const me = useMe()
  const checkingIsSlow = useHasElapsed(checkingIndicatorDelayMs)
  const state: MeState = me.isSuccess
    ? { kind: 'loaded', me: me.data }
    : !me.isError
      ? { kind: 'loading' }
      : me.error instanceof ApiError && me.error.status === 403
        ? { kind: 'noAccess' }
        : { kind: 'failed' }

  // Who's signed in comes from sign-in itself, so it's known from the first frame and
  // never changes. The API only supplies the role (it enforces access on every request
  // anyway), so its answer can't change or move anything else in the header.
  const primary = user.name ?? user.username ?? 'Signed in'
  const secondary = user.name && user.username ? user.username : null

  return (
    <div className="account">
      <p role="status" aria-label="Account" className="account-card">
        {(user.name || user.username) && <span className="sr-only">Signed in as </span>}
        <strong className="account-name" title={primary}>
          {primary}
        </strong>{' '}
        <span className="account-meta">
          <span className="account-username" title={secondary ?? undefined}>
            {secondary}
          </span>{' '}
          <RoleBadge state={state} checkingIsSlow={checkingIsSlow} user={user} />
        </span>
      </p>
      <button type="button" onClick={auth.signOut}>
        Sign out
      </button>
    </div>
  )
}

/** True once `ms` milliseconds have passed since the component appeared. */
function useHasElapsed(ms: number): boolean {
  const [elapsed, setElapsed] = useState(false)
  useEffect(() => {
    const timer = setTimeout(() => setElapsed(true), ms)
    return () => clearTimeout(timer)
  }, [ms])
  return elapsed
}

/**
 * The role, in a slot whose width comes from an invisible copy of the widest label it
 * can show (a CSS pseudo-element, so it isn't part of the page's text). Loading, any
 * role and the two problem states all occupy exactly the same space.
 */
function RoleBadge({ state, checkingIsSlow, user }: { state: MeState; checkingIsSlow: boolean; user: SignedInUser }) {
  switch (state.kind) {
    case 'loading':
      return (
        <span className="role-badge" data-widest={widestBadgeLabel}>
          {checkingIsSlow && (
            <span className="spinner">
              <span className="sr-only">Checking your access…</span>
            </span>
          )}
        </span>
      )
    case 'loaded':
      return (
        <span className="role-badge" data-widest={widestBadgeLabel}>
          <span className="fade-in">{state.me.roles.map((role) => roleNames[role] ?? role).join(', ')}</span>
        </span>
      )
    case 'noAccess':
    case 'failed': {
      // Name the account in the explanation, so the user can tell whether they signed in with the wrong one.
      const explanation =
        state.kind === 'noAccess'
          ? `${user.username ?? user.name ?? 'This account'} doesn't have access to the family tree.`
          : "The family tree couldn't be reached. Please try again later."
      return (
        <span className="role-badge role-badge--problem" data-widest={widestBadgeLabel} title={explanation}>
          <span className="fade-in" aria-hidden="true">
            {problemLabels[state.kind]}
          </span>
          <span className="sr-only">{explanation}</span>
        </span>
      )
    }
  }
}
