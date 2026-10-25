import { useEffect, useState, type ReactNode } from 'react'
import { ApiError, fetchMe, type Me } from '../../shared/api/me.ts'
import type { SignedInUser } from '../../shared/auth/auth.ts'
import { useAuth } from '../../shared/auth/authContext.ts'

const roleNames: Record<string, string> = {
  'Family.Admin': 'Family admin',
  'Family.Viewer': 'Family viewer',
}

/** Sizes the role badge, so it's the same width whether loading or showing any role. */
const widestRoleName = Object.values(roleNames).reduce((widest, name) => (name.length > widest.length ? name : widest))

type MeState =
  | { kind: 'loading' }
  | { kind: 'loaded'; me: Me }
  | { kind: 'noAccess' }
  | { kind: 'failed' }

/** Sign in and out, and who the API says you are once signed in. */
export function Account() {
  const auth = useAuth()

  if (!auth.isConfigured) {
    return <p className="account">Sign-in isn't set up in this copy of the app.</p>
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
  const [state, setState] = useState<MeState>({ kind: 'loading' })
  const checkingIsSlow = useHasElapsed(checkingIndicatorDelayMs)

  useEffect(() => {
    let active = true
    auth
      .getAccessToken()
      .then(fetchMe)
      .then(
        (me) => active && setState({ kind: 'loaded', me }),
        (error: unknown) =>
          active && setState(error instanceof ApiError && error.status === 403 ? { kind: 'noAccess' } : { kind: 'failed' }),
      )
    return () => {
      active = false
    }
  }, [auth])

  return (
    <div className="account">
      <p role="status" aria-label="Account">
        <AccountStatus state={state} user={user} checkingIsSlow={checkingIsSlow} />
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
 * Who's signed in is known from sign-in itself, so it's shown straight away and stays
 * put. The role, confirmed by the API, goes in a badge that takes its final size from
 * the start, so nothing shifts when it arrives.
 */
function AccountStatus({ state, user, checkingIsSlow }: { state: MeState; user: SignedInUser; checkingIsSlow: boolean }) {
  switch (state.kind) {
    case 'loading':
      return (
        <>
          <Identity name={user.name} username={user.username} />{' '}
          <RoleBadge>
            {checkingIsSlow && (
              <span className="spinner">
                <span className="sr-only">Checking your access…</span>
              </span>
            )}
          </RoleBadge>
        </>
      )
    case 'loaded': {
      // The API's view of the user, which is what access is actually based on. It
      // matches what's already shown, so in practice only the role appears.
      const roles = state.me.roles.map((role) => roleNames[role] ?? role).join(', ')
      return (
        <>
          <Identity name={state.me.name} username={state.me.username} />{' '}
          <RoleBadge>
            <span className="fade-in">{roles}</span>
          </RoleBadge>
        </>
      )
    }
    case 'noAccess':
      // Name the account, so the user can tell whether they signed in with the wrong one.
      return (
        <span className="fade-in">
          You're signed in as {user.username ?? user.name ?? 'an account'}, but that account doesn't have access to the
          family tree.
        </span>
      )
    case 'failed':
      return (
        <>
          <Identity name={user.name} username={user.username} />
          <span className="fade-in">, but the family tree couldn't be reached. Please try again later.</span>
        </>
      )
  }
}

/**
 * A fixed-size slot for the role. Its width comes from an invisible copy of the
 * longest role name (a CSS pseudo-element, so it isn't part of the page's text),
 * and its content is drawn on top of that in the same grid cell.
 */
function RoleBadge({ children }: { children: ReactNode }) {
  return (
    <span className="role-badge" data-widest={widestRoleName}>
      {children}
    </span>
  )
}

/**
 * "Signed in as <name> · <username>". Profiles don't always have a display name, so
 * fall back to the username, and to plain "Signed in" if there's neither.
 */
function Identity({ name, username }: { name: string | null; username: string | null }) {
  if (!name && !username) {
    return <>Signed in</>
  }
  return (
    <>
      Signed in as <strong>{name ?? username}</strong>
      {name && username && (
        <>
          {' · '}
          <span className="account-username">{username}</span>
        </>
      )}
    </>
  )
}
