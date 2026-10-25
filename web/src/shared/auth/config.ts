/**
 * Sign-in settings, baked in at build time from VITE_* environment variables.
 * In Azure they come from the GitHub `production` environment variables; locally,
 * from web/.env.local (see docs/setup-entra.md). Neither value is a secret.
 */
export interface SignInConfig {
  /** Client ID of the "Family Tree" Entra app registration. */
  clientId: string
  /** Where users sign in: the family's Entra tenant. */
  authority: string
  /** The permission requested in access tokens for the family tree API. */
  apiScope: string
}

export function readSignInConfig(env: ImportMetaEnv = import.meta.env): SignInConfig | null {
  const clientId = env.VITE_ENTRA_CLIENT_ID
  const tenantId = env.VITE_ENTRA_TENANT_ID
  if (!clientId || !tenantId) {
    return null
  }

  return {
    clientId,
    authority: `https://login.microsoftonline.com/${tenantId}`,
    // Matches the identifier URI and scope in infra/entra.bicep.
    apiScope: `api://${tenantId}/familytree/Family.Read`,
  }
}
