import { useMutation, useQueryClient } from '@tanstack/react-query'
import { useState } from 'react'
import { SaveError } from './AddRelative.tsx'
import { refreshFamilies } from './families.ts'

interface ConfirmRemoveProps {
  /** What the button says, when "Remove" alone wouldn't be clear from where it sits. */
  label?: string
  /** What the button is called for screen readers, which can't see where it sits. */
  buttonName: string
  question: string
  remove: () => Promise<void>
}

/** A small Remove button that asks first, for undoing a link between people. */
export function ConfirmRemove({ label = 'Remove', buttonName, question, remove }: ConfirmRemoveProps) {
  const queryClient = useQueryClient()
  const [confirming, setConfirming] = useState(false)

  const removal = useMutation({
    mutationFn: remove,
    onSuccess: async () => {
      await refreshFamilies(queryClient)
      setConfirming(false)
    },
  })

  if (!confirming) {
    return (
      <button type="button" className="small" aria-label={buttonName} onClick={() => setConfirming(true)}>
        {label}
      </button>
    )
  }

  return (
    <div className="notice timeline-notice">
      <p>{question}</p>
      <SaveError error={removal.error} />
      <div className="form-buttons">
        <button type="button" disabled={removal.isPending} onClick={() => removal.mutate()}>
          Yes, remove
        </button>
        <button type="button" onClick={() => setConfirming(false)}>
          Cancel
        </button>
      </div>
    </div>
  )
}
