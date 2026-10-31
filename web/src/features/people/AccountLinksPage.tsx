import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Link } from 'react-router'
import { useApi } from '../../shared/api/client.ts'
import { ConfirmRemove } from './ConfirmRemove.tsx'

interface AccountLink {
  accountId: string
  displayName: string | null
  username: string | null
  isApproved: boolean
  personId: string
  personName: string
}

const linksUrl = '/api/v1/admin/account-links'

/** Requests from family members to be linked to someone in the tree, and the links made so far. */
export function AccountLinksPage() {
  const api = useApi()
  const links = useQuery({ queryKey: ['accountLinks'], queryFn: () => api<AccountLink[]>(linksUrl) })

  return (
    <>
      <p>
        <Link to="/people">Back to people</Link>
      </p>
      <h1>Linked accounts</h1>
      <p>
        A linked account sees that person's own details in full. Only approve a request when you know who the account
        belongs to.
      </p>

      {links.isPending && <p role="status">Loading…</p>}
      {links.isError && <p role="alert">The linked accounts couldn't be loaded. Please try again.</p>}
      {links.data?.length === 0 && <p>Nobody has asked to be linked yet.</p>}

      {links.data && links.data.length > 0 && (
        <ul className="timeline">
          {links.data.map((link) => (
            <AccountLinkRow key={link.accountId} link={link} />
          ))}
        </ul>
      )}
    </>
  )
}

function AccountLinkRow({ link }: { link: AccountLink }) {
  const api = useApi()
  const queryClient = useQueryClient()
  const account = link.displayName ?? link.username ?? 'An unnamed account'
  const refresh = async () => {
    await queryClient.invalidateQueries({ queryKey: ['accountLinks'] })
    await queryClient.invalidateQueries({ queryKey: ['myLink'] })
  }

  const approve = useMutation({
    mutationFn: () => api<void>(`${linksUrl}/${link.accountId}/approve`, { method: 'POST' }),
    onSuccess: refresh,
  })

  return (
    <li className="relatives-row">
      <span>
        <strong>{account}</strong>
        {link.username && link.username !== account && ` (${link.username})`}
        {link.isApproved ? ' is ' : ' says they are '}
        <Link to={`/people/${link.personId}`}>{link.personName}</Link>
      </span>
      <span className="form-buttons">
        {!link.isApproved && (
          <button type="button" className="small" aria-label={`Approve ${account}`} disabled={approve.isPending} onClick={() => approve.mutate()}>
            Approve
          </button>
        )}
        <ConfirmRemove
          buttonName={`Remove ${account}`}
          question={`Remove the link for ${account}? They'll stop seeing this person's details.`}
          remove={async () => {
            await api<void>(`${linksUrl}/${link.accountId}`, { method: 'DELETE' })
            await refresh()
          }}
        />
      </span>
      {approve.isError && (
        <p role="alert" className="field-error">
          This couldn't be approved. Please try again.
        </p>
      )}
    </li>
  )
}
