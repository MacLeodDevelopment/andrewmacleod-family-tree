/**
 * What the rest of the app needs to know about sign-in. Components depend on this,
 * never on MSAL directly, so the sign-in library can change and tests can supply
 * a pretend user.
 */
export interface Auth {
  /** False when this build has no sign-in settings (for example, local development without them). */
  readonly isConfigured: boolean
  /** The signed-in user, or null when nobody is signed in. */
  readonly user: SignedInUser | null
  /** Why the last sign-in attempt failed, if it did. */
  readonly signInError: string | null
  /** Sends the user to Microsoft's sign-in page; they come back to this page afterwards. */
  signIn(): void
  /** Signs the user out of the app and of Microsoft, then returns to this page. */
  signOut(): void
  /** An access token for calling the family tree API as the signed-in user. */
  getAccessToken(): Promise<string>
}

export interface SignedInUser {
  /** Display name from the user's profile; null if they don't have one. */
  readonly name: string | null
  /** The account they signed in with, usually an email address. */
  readonly username: string | null
}

/** Used when the build has no sign-in settings: nobody can sign in. */
export const notConfiguredAuth: Auth = {
  isConfigured: false,
  user: null,
  signInError: null,
  signIn: () => {},
  signOut: () => {},
  getAccessToken: () => Promise.reject(new Error('Sign-in is not configured.')),
}
