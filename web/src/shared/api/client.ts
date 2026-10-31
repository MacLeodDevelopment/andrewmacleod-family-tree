import { useCallback } from 'react'
import { useAuth } from '../auth/authContext.ts'
import { apiUrl } from './apiUrl.ts'

/** The API's error body: RFC 9457 problem details, plus what some errors add. */
export interface Problem {
  title?: string
  detail?: string
  /** Validation messages by field, such as `names[0].givenName`. */
  errors?: Record<string, string[]>
  /** People who may already be this person, when adding someone is stopped. */
  matches?: unknown[]
}

/** The API refused or failed a request; `status` is the HTTP status code. */
export class ApiError extends Error {
  readonly status: number
  readonly problem: Problem

  constructor(status: number, problem: Problem = {}) {
    super(problem.detail ?? problem.title ?? `The API responded with ${status}.`)
    this.status = status
    this.problem = problem
  }
}

/** Calls the API as the signed-in user. Returns the parsed body, if there is one. */
export async function apiRequest<T>(path: string, accessToken: string, init: RequestInit = {}): Promise<T> {
  const response = await fetch(apiUrl(path), {
    ...init,
    headers: {
      Authorization: `Bearer ${accessToken}`,
      ...(init.body ? { 'Content-Type': 'application/json' } : {}),
    },
  })

  if (!response.ok) {
    const isProblem = response.headers.get('Content-Type')?.includes('json')
    throw new ApiError(response.status, isProblem ? ((await response.json()) as Problem) : {})
  }

  return (response.status === 204 ? undefined : await response.json()) as T
}

/** {@link apiRequest}, getting the signed-in user's token first. */
export function useApi() {
  const auth = useAuth()

  return useCallback(
    async <T>(path: string, init?: RequestInit) => apiRequest<T>(path, await auth.getAccessToken(), init),
    [auth],
  )
}
