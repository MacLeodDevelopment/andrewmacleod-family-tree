import { useQuery } from '@tanstack/react-query'
import { useAuth } from '../auth/authContext.ts'
import { useApi } from './client.ts'

export interface Me {
  /** Display name from the user's profile; null if they don't have one. */
  name: string | null
  /** The account they signed in with, usually an email address; null if unknown. */
  username: string | null
  roles: string[]
}

export const adminRole = 'Family.Admin'

/** Who the API says the signed-in user is, shared by everything that needs to know. */
export function useMe() {
  const auth = useAuth()
  const api = useApi()

  return useQuery({
    queryKey: ['me'],
    queryFn: () => api<Me>('/api/v1/me'),
    enabled: auth.user !== null,
  })
}
