import { render, screen } from '@testing-library/react'
import { http, HttpResponse } from 'msw'
import { describe, expect, it } from 'vitest'
import App from './App.tsx'
import { server } from './test/server.ts'

describe('App shell', () => {
  it('shows the family tree title', () => {
    server.use(http.get('*/health', () => HttpResponse.text('Healthy')))

    render(<App />)

    expect(screen.getByRole('heading', { level: 1, name: 'MacLeod Family Tree' })).toBeInTheDocument()
  })

  it('tells the user when the API is healthy', async () => {
    server.use(http.get('*/health', () => HttpResponse.text('Healthy')))

    render(<App />)

    expect(await screen.findByRole('status')).toHaveTextContent('API is healthy')
  })

  it('tells the user when the API is unavailable', async () => {
    server.use(http.get('*/health', () => new HttpResponse(null, { status: 503 })))

    render(<App />)

    expect(await screen.findByRole('status')).toHaveTextContent('API is unavailable')
  })
})
