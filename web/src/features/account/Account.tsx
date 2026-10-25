import { useEffect, useState } from 'react'
import { ApiError, fetchMe, type Me } from '../../shared/api/me.ts'
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

  return <SignedIn />
}

function SignedIn() {
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
        {describe(state, auth.user?.name ?? '')}
      </p>
      <button type="button" onClick={auth.signOut}>
        Sign out
      </button>
    </div>
  )
}

function describe(state: MeState, name: string): string {
  switch (state.kind) {
    case 'loading':
      return `Signed in as ${name}. Checking your access…`
    case 'loaded': {
      const roles = state.me.roles.map((role) => roleNames[role] ?? role).join(', ')
      return `Signed in as ${state.me.name} (${roles})`
    }
    case 'noAccess':
      return `Signed in as ${name}, but your account doesn't have access to the family tree.`
    case 'failed':
      return `Signed in as ${name}, but the family tree couldn't be reached. Please try again later.`
  }
}
