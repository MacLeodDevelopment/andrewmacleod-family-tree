import { apiUrl } from './apiUrl.ts'

export interface Me {
  name: string
  roles: string[]
}

/** The API refused or failed a request; `status` is the HTTP status code. */
export class ApiError extends Error {
  readonly status: number

  constructor(status: number) {
    super(`The API responded with ${status}.`)
    this.status = status
  }
}

/** Who the API says the signed-in user is, and their family roles. */
export async function fetchMe(accessToken: string): Promise<Me> {
  const response = await fetch(apiUrl('/api/v1/me'), {
    headers: { Authorization: `Bearer ${accessToken}` },
  })
  if (!response.ok) {
    throw new ApiError(response.status)
  }
  return (await response.json()) as Me
}
