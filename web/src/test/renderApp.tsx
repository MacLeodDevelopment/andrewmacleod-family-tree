import { render } from '@testing-library/react'
import { vi } from 'vitest'
import App from '../App.tsx'
import type { Auth } from '../shared/auth/auth.ts'
import { AuthContext } from '../shared/auth/authContext.ts'

/** The access token the pretend signed-in user presents to the API. */
export const testAccessToken = 'test-access-token'

/**
 * Pretend sign-in state, standing in for Microsoft Entra ID. Defaults to signed out;
 * `signIn` and `signOut` are spies, so tests can check they were requested.
 */
export function fakeAuth(overrides: Partial<Auth> = {}): Auth {
  return {
    isConfigured: true,
    user: null,
    signInError: null,
    signIn: vi.fn(),
    signOut: vi.fn(),
    getAccessToken: () => Promise.resolve(testAccessToken),
    ...overrides,
  }
}

export function renderApp(auth: Auth = fakeAuth()) {
  return render(
    <AuthContext.Provider value={auth}>
      <App />
    </AuthContext.Provider>,
  )
}
