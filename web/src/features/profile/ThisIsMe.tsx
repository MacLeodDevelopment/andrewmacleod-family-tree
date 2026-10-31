import { useMutation, useQueryClient } from '@tanstack/react-query'
import { ApiError, useApi } from '../../shared/api/client.ts'
import { useMyLink, type MyLink } from '../../shared/api/myLink.ts'
import { profilesUrl } from './profile.ts'

/** Lets someone say which person in the tree they are. An admin then confirms it. */
export function ThisIsMe({ personId }: { personId: string }) {
  const api = useApi()
  const queryClient = useQueryClient()
  const link = useMyLink()

  const claim = useMutation({
    mutationFn: () => api<MyLink>(`${profilesUrl}/${personId}/claim`, { method: 'POST' }),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['myLink'] }),
  })

  if (link.data === undefined || link.data?.isApproved && link.data.personId !== personId) {
    return null
  }

  if (link.data?.personId === personId) {
    return link.data.isApproved ? <p>This is you.</p> : <p role="status">Waiting for an admin to confirm this is you.</p>
  }

  return (
    <>
      <p>
        <button type="button" className="small" disabled={claim.isPending} onClick={() => claim.mutate()}>
          This is me
        </button>
      </p>
      {claim.isError && (
        <p role="alert" className="field-error">
          {claim.error instanceof ApiError && claim.error.problem.title
            ? claim.error.problem.title
            : "This couldn't be sent. Please try again."}
        </p>
      )}
    </>
  )
}
