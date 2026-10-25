import { screen, waitFor } from '@testing-library/react'
import { http, HttpResponse } from 'msw'
import { describe, expect, it } from 'vitest'
import { renderApp } from './test/renderApp.tsx'
import { server } from './test/server.ts'

describe('App shell', () => {
  it('shows the family tree title', () => {
    renderApp()

    expect(screen.getByRole('heading', { level: 1, name: 'MacLeod Family Tree' })).toBeInTheDocument()
  })

  it('tells the user when the API is healthy', async () => {
    renderApp()

    await waitFor(() => expect(screen.getByRole('status', { name: 'API status' })).toHaveTextContent('API is healthy'))
  })

  it('tells the user when the API is unavailable', async () => {
    server.use(http.get('*/health', () => new HttpResponse(null, { status: 503 })))

    renderApp()

    await waitFor(() =>
      expect(screen.getByRole('status', { name: 'API status' })).toHaveTextContent('API is unavailable'),
    )
  })
})
