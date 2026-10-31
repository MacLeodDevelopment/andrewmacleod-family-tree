import { useQuery } from '@tanstack/react-query'
import { useApi } from './client.ts'
import { useMe } from './me.ts'

/** Who the signed-in user has asked to be, or has been approved as, in the tree. */
export interface MyLink {
  personId: string
  isApproved: boolean
}

export function useMyLink() {
  const api = useApi()
  const me = useMe()

  return useQuery({
    queryKey: ['myLink'],
    queryFn: async () => (await api<MyLink | undefined>('/api/v1/me/link')) ?? null,
    enabled: (me.data?.roles.length ?? 0) > 0,
  })
}
