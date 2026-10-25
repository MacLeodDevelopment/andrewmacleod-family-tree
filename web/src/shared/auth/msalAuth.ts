import {
  createStandardPublicClientApplication,
  InteractionRequiredAuthError,
  type AccountInfo,
  type IPublicClientApplication,
} from '@azure/msal-browser'
import type { Auth } from './auth.ts'
import type { SignInConfig } from './config.ts'

/**
 * Sign-in with Microsoft Entra ID through MSAL (Microsoft Authentication Library),
 * using the authorisation code flow with PKCE and full-page redirects.
 *
 * Sign-in and sign-out leave the page and come back, so the signed-in user can only
 * change on a fresh page load: this runs once at startup and the result never changes.
 */
export async function createMsalAuth(config: SignInConfig): Promise<Auth> {
  const msal = await createStandardPublicClientApplication({
    auth: {
      clientId: config.clientId,
      authority: config.authority,
      redirectUri: window.location.origin,
      postLogoutRedirectUri: window.location.origin,
    },
    // Tokens last for the browser tab only, not across browser restarts.
    cache: { cacheLocation: 'sessionStorage' },
  })

  // When returning from Microsoft's sign-in page, this completes the sign-in.
  let signInError: string | null = null
  try {
    const result = await msal.handleRedirectPromise()
    if (result?.account) {
      msal.setActiveAccount(result.account)
    }
  } catch (error) {
    signInError = describeSignInError(error)
  }

  const account = msal.getActiveAccount() ?? msal.getAllAccounts()[0] ?? null
  if (account) {
    msal.setActiveAccount(account)
  }

  return {
    isConfigured: true,
    user: account ? { name: account.name ?? account.username } : null,
    signInError,
    signIn: () => void msal.loginRedirect({ scopes: [config.apiScope] }),
    signOut: () => void msal.logoutRedirect({ account }),
    getAccessToken: () => acquireToken(msal, account, config.apiScope),
  }
}

async function acquireToken(msal: IPublicClientApplication, account: AccountInfo | null, scope: string) {
  if (!account) {
    throw new Error('Nobody is signed in.')
  }

  try {
    // From MSAL's cache, refreshing behind the scenes when it has expired.
    const result = await msal.acquireTokenSilent({ scopes: [scope], account })
    return result.accessToken
  } catch (error) {
    if (error instanceof InteractionRequiredAuthError) {
      // The session has ended (or consent is needed): go back through sign-in.
      await msal.acquireTokenRedirect({ scopes: [scope], account })
    }
    throw error
  }
}

function describeSignInError(error: unknown): string {
  const message = error instanceof Error ? error.message : String(error)
  // AADSTS50105: the user has no role in the app ("Assignment required").
  if (message.includes('AADSTS50105')) {
    return "Your account hasn't been given access to the family tree."
  }
  return 'Sign-in failed. Please try again.'
}
