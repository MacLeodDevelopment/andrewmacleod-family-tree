import { createContext, useContext } from 'react'
import { notConfiguredAuth, type Auth } from './auth.ts'

/** Provides the app's sign-in state: `<AuthContext.Provider value={auth}>`. */
export const AuthContext = createContext<Auth>(notConfiguredAuth)

export function useAuth(): Auth {
  return useContext(AuthContext)
}
