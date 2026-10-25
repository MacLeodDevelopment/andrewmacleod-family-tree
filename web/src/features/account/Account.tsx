import { useEffect, useState } from 'react'
import { ApiError, fetchMe, type Me } from '../../shared/api/me.ts'
import type { SignedInUser } from '../../shared/auth/auth.ts'
import { useAuth } from '../../shared/auth/authContext.ts'

const roleNames: Record<string, string> = {
  'Family.Admin': 'Family admin',
  'Family.Viewer': 'Family viewer',
}

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

function SignedIn({ user }: { user: SignedInUser }) {
  const auth = useAuth()
  const [state, setState] = useState<MeState>({ kind: 'loading' })

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
        <AccountStatus state={state} user={user} />
      </p>
      <button type="button" onClick={auth.signOut}>
        Sign out
      </button>
    </div>
  )
}

function AccountStatus({ state, user }: { state: MeState; user: SignedInUser }) {
  switch (state.kind) {
    case 'loading':
      return (
        <>
          <Identity name={user.name} username={user.username} />. Checking your access…
        </>
      )
    case 'loaded': {
      // The API's view of the user, which is what access is actually based on.
      const roles = state.me.roles.map((role) => roleNames[role] ?? role).join(', ')
      return (
        <>
          <Identity name={state.me.name} username={state.me.username} />
          {' · '}
          {roles}
        </>
      )
    }
    case 'noAccess':
      // Name the account, so the user can tell whether they signed in with the wrong one.
      return (
        <>
          You're signed in as {user.username ?? user.name ?? 'an account'}, but that account doesn't have access to the
          family tree.
        </>
      )
    case 'failed':
      return (
        <>
          <Identity name={user.name} username={user.username} />, but the family tree couldn't be reached. Please try
          again later.
        </>
      )
  }
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
