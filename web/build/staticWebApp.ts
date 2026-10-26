/**
 * HTTP security headers for the web app, and the Azure Static Web Apps configuration
 * that serves them. Used at build time by vite.config.ts, which writes the result to
 * dist/staticwebapp.config.json, and by `npm run preview`, so the same headers can be
 * tried locally. (The Vite dev server doesn't send them: its hot reloading needs
 * inline scripts that the policy forbids.)
 */

/** Microsoft Entra ID: the web app talks to it to sign in and to get tokens. */
const signInOrigin = 'https://login.microsoftonline.com'

export interface SecurityHeaderOptions {
  /**
   * Where the API is, from VITE_API_BASE_URL. Unset locally, where the API is reached
   * through the same origin as the page (the Vite proxy).
   */
  apiBaseUrl?: string
}

export function securityHeaders({ apiBaseUrl }: SecurityHeaderOptions): Record<string, string> {
  const apiOrigin = apiBaseUrl ? new URL(apiBaseUrl).origin : null

  // Content Security Policy: the browser refuses anything not listed here. If an
  // attacker ever got script onto the page, it couldn't run inline, load code from
  // elsewhere, or send data anywhere but our own API and Microsoft sign-in.
  const contentSecurityPolicy = [
    "default-src 'self'",
    "script-src 'self'",
    "style-src 'self'",
    "img-src 'self' data:",
    "font-src 'self'",
    ['connect-src', "'self'", apiOrigin, signInOrigin].filter(Boolean).join(' '),
    // MSAL can fall back to a hidden iframe on Microsoft's sign-in page to renew tokens.
    `frame-src ${signInOrigin}`,
    "object-src 'none'",
    "base-uri 'self'",
    "form-action 'self'",
    // Nobody may show this site inside a frame on theirs (click-jacking).
    "frame-ancestors 'none'",
    'upgrade-insecure-requests',
  ].join('; ')

  return {
    'Content-Security-Policy': contentSecurityPolicy,
    // Always use HTTPS for this site, for a year.
    'Strict-Transport-Security': 'max-age=31536000; includeSubDomains',
    // Don't guess file types: a file is only ever treated as what the server says it is.
    'X-Content-Type-Options': 'nosniff',
    // Other sites learn only that a visitor came from here, never which page.
    'Referrer-Policy': 'strict-origin-when-cross-origin',
    // Features the app never needs are switched off outright.
    'Permissions-Policy': 'camera=(), microphone=(), geolocation=(), payment=(), usb=()',
    // Pages from other sites can't get a handle on this one's window.
    'Cross-Origin-Opener-Policy': 'same-origin',
    // A private site: ask search engines not to index it.
    'X-Robots-Tag': 'noindex, nofollow',
  }
}

/**
 * staticwebapp.config.json. See
 * https://learn.microsoft.com/azure/static-web-apps/configuration
 */
export function staticWebAppConfig(headers: Record<string, string>) {
  return {
    globalHeaders: headers,
    // The app decides what each path shows (for example /people/123 later), so any
    // path that isn't a real file serves the app itself rather than a 404.
    navigationFallback: {
      rewrite: '/index.html',
      exclude: ['/assets/*', '/*.{svg,png,ico,json,txt}'],
    },
  }
}
