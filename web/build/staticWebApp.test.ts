import { describe, expect, it } from 'vitest'
import { securityHeaders, staticWebAppConfig } from './staticWebApp.ts'

/** The Content Security Policy as a map of directive name to its sources. */
function policy(apiBaseUrl?: string): Map<string, string[]> {
  const csp = securityHeaders({ apiBaseUrl })['Content-Security-Policy']
  return new Map(
    csp.split(';').map((directive) => {
      const [name, ...sources] = directive.trim().split(/\s+/)
      return [name, sources] as const
    }),
  )
}

describe('Security headers', () => {
  it('let the page talk only to itself, the API and Microsoft sign-in', () => {
    expect(policy('https://api.family.example.test').get('connect-src')).toEqual([
      "'self'",
      'https://api.family.example.test',
      'https://login.microsoftonline.com',
    ])
  })

  it('allow only the API origin, not the rest of its address', () => {
    expect(policy('https://api.family.example.test/some/path').get('connect-src')).toContain(
      'https://api.family.example.test',
    )
  })

  it('need no API entry locally, where the API is reached through the page\'s own origin', () => {
    expect(policy(undefined).get('connect-src')).toEqual(["'self'", 'https://login.microsoftonline.com'])
  })

  it('only run scripts the site itself serves, never inline ones', () => {
    expect(policy().get('script-src')).toEqual(["'self'"])
    expect(securityHeaders({})['Content-Security-Policy']).not.toContain('unsafe-inline')
  })

  it('stop other sites showing the family tree in a frame', () => {
    expect(policy().get('frame-ancestors')).toEqual(["'none'"])
  })

  it('ask search engines not to index the site', () => {
    expect(securityHeaders({})['X-Robots-Tag']).toContain('noindex')
  })
})

describe('Static Web App configuration', () => {
  it('serves every header on every response', () => {
    const headers = securityHeaders({})

    expect(staticWebAppConfig(headers).globalHeaders).toEqual(headers)
  })

  it('serves the app for paths it handles itself, but not for missing files', () => {
    const { navigationFallback } = staticWebAppConfig({})

    expect(navigationFallback.rewrite).toBe('/index.html')
    expect(navigationFallback.exclude).toContain('/assets/*')
  })
})
