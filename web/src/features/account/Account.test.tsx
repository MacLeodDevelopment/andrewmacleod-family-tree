import { act, fireEvent, screen, waitFor } from '@testing-library/react'
import { http, HttpResponse } from 'msw'
import { describe, expect, it, vi } from 'vitest'
import { notConfiguredAuth } from '../../shared/auth/auth.ts'
import { checkingIndicatorDelayMs } from './Account.tsx'
import { fakeAuth, renderApp, testAccessToken } from '../../test/renderApp.tsx'
import { server } from '../../test/server.ts'

const signedIn = { user: { name: 'Jean MacLeod', username: 'jean@example.test' } }

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

/** The API hasn't answered yet (and won't, during the test). */
function meIsStillLoading() {
  server.use(http.get('*/api/v1/me', () => new Promise<never>(() => {})))
}

const accountStatus = () => screen.getByRole('status', { name: 'Account' })

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

  it('shows who is signed in, with which account, and their role once the API confirms it', async () => {
    meResponds({ name: 'Jean MacLeod', username: 'jean@example.test', roles: ['Family.Admin'] })

    renderApp(fakeAuth(signedIn))

    await waitFor(() => expect(accountStatus()).toHaveTextContent('Signed in as Jean MacLeod jean@example.test Family admin'))
  })

  it('keeps showing the signed-in account as it was, however the API describes it', async () => {
    // A token without a name or username claim must not change what's already on screen.
    meResponds({ name: null, username: null, roles: ['Family.Admin'] })

    renderApp(fakeAuth(signedIn))

    await waitFor(() => expect(accountStatus()).toHaveTextContent('Signed in as Jean MacLeod jean@example.test Family admin'))
  })

  it('shows who is signed in straight away, without a loading message, while access is checked', () => {
    meIsStillLoading()

    renderApp(fakeAuth(signedIn))

    expect(accountStatus()).toHaveTextContent(/^Signed in as Jean MacLeod jean@example.test$/)
  })

  it('shows a checking indicator only when the check is taking a noticeable time', () => {
    vi.useFakeTimers({ toFake: ['setTimeout', 'clearTimeout'] })
    try {
      meIsStillLoading()
      renderApp(fakeAuth(signedIn))

      act(() => vi.advanceTimersByTime(checkingIndicatorDelayMs - 1))
      expect(accountStatus()).not.toHaveTextContent('Checking your access')

      act(() => vi.advanceTimersByTime(1))
      expect(accountStatus()).toHaveTextContent('Signed in as Jean MacLeod jean@example.test Checking your access…')
    } finally {
      vi.useRealTimers()
    }
  })

  it('identifies users by their account when their profile has no display name', async () => {
    meResponds({ name: null, username: 'jean@example.test', roles: ['Family.Viewer'] })

    renderApp(fakeAuth({ user: { name: null, username: 'jean@example.test' } }))

    await waitFor(() => expect(accountStatus()).toHaveTextContent(/^Signed in as jean@example.test Family viewer$/))
  })

  it('still makes sense when neither a name nor a username is known', async () => {
    meResponds({ name: null, username: null, roles: ['Family.Viewer'] })

    renderApp(fakeAuth({ user: { name: null, username: null } }))

    await waitFor(() => expect(accountStatus()).toHaveTextContent(/^Signed in Family viewer$/))
  })

  it('tells signed-in users without a family role which account has no access', async () => {
    meResponds({}, 403)

    renderApp(fakeAuth(signedIn))

    await waitFor(() => expect(accountStatus()).toHaveTextContent('No access'))
    expect(accountStatus()).toHaveTextContent("jean@example.test doesn't have access to the family tree.")
  })

  it('says when the family tree cannot be reached', async () => {
    meResponds({}, 500)

    renderApp(fakeAuth(signedIn))

    await waitFor(() => expect(accountStatus()).toHaveTextContent('Unavailable'))
    expect(accountStatus()).toHaveTextContent("The family tree couldn't be reached. Please try again later.")
  })

  it('lets signed-in users sign out', () => {
    meResponds({ name: 'Jean MacLeod', username: 'jean@example.test', roles: ['Family.Viewer'] })
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
