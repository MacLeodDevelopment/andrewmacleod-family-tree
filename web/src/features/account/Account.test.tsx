import { fireEvent, screen, waitFor } from '@testing-library/react'
import { http, HttpResponse } from 'msw'
import { describe, expect, it } from 'vitest'
import { notConfiguredAuth } from '../../shared/auth/auth.ts'
import { fakeAuth, renderApp, testAccessToken } from '../../test/renderApp.tsx'
import { server } from '../../test/server.ts'

const signedIn = { user: { name: 'Jean MacLeod' } }

/** The pretend API only answers /me for requests carrying the signed-in user's token. */
function meResponds(body: object, status = 200) {
  server.use(
    http.get('*/api/v1/me', ({ request }) =>
      request.headers.get('Authorization') === `Bearer ${testAccessToken}`
        ? HttpResponse.json(body, { status })
        : new HttpResponse(null, { status: 401 }),
    ),
  )
}

describe('Account', () => {
  it('invites visitors to sign in', () => {
    const auth = fakeAuth()
    renderApp(auth)

    fireEvent.click(screen.getByRole('button', { name: 'Sign in' }))

    expect(auth.signIn).toHaveBeenCalled()
  })

  it('explains why the last sign-in failed', () => {
    renderApp(fakeAuth({ signInError: "Your account hasn't been given access to the family tree." }))

    expect(screen.getByRole('alert')).toHaveTextContent("Your account hasn't been given access to the family tree.")
    expect(screen.getByRole('button', { name: 'Sign in' })).toBeInTheDocument()
  })

  it('shows who is signed in, and their role, as confirmed by the API', async () => {
    meResponds({ name: 'Jean MacLeod', roles: ['Family.Admin'] })

    renderApp(fakeAuth(signedIn))

    await waitFor(() =>
      expect(screen.getByRole('status', { name: 'Account' })).toHaveTextContent('Signed in as Jean MacLeod (Family admin)'),
    )
  })

  it('tells signed-in users without a family role that they have no access', async () => {
    meResponds({}, 403)

    renderApp(fakeAuth(signedIn))

    await waitFor(() =>
      expect(screen.getByRole('status', { name: 'Account' })).toHaveTextContent("doesn't have access to the family tree"),
    )
  })

  it('says when the family tree cannot be reached', async () => {
    meResponds({}, 500)

    renderApp(fakeAuth(signedIn))

    await waitFor(() =>
      expect(screen.getByRole('status', { name: 'Account' })).toHaveTextContent("the family tree couldn't be reached"),
    )
  })

  it('lets signed-in users sign out', () => {
    meResponds({ name: 'Jean MacLeod', roles: ['Family.Viewer'] })
    const auth = fakeAuth(signedIn)
    renderApp(auth)

    fireEvent.click(screen.getByRole('button', { name: 'Sign out' }))

    expect(auth.signOut).toHaveBeenCalled()
  })

  it('says so when sign-in is not set up', () => {
    renderApp(notConfiguredAuth)

    expect(screen.getByText("Sign-in isn't set up in this copy of the app.")).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Sign in' })).not.toBeInTheDocument()
  })
})
