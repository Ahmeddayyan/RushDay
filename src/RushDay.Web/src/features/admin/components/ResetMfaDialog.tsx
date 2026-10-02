import type { AccountView } from '@/api/types/common'
import { toast } from '@/lib/toast'

import { useAccountAction } from '../hooks/useAccounts'

import { ConfirmDialog } from './ConfirmDialog'

/** "Reset two-step verification" for one account (02-api.md section 8.5, `reset-mfa`). */
export function ResetMfaDialog({
  account,
  open,
  onOpenChange,
}: {
  account: AccountView
  open: boolean
  onOpenChange: (open: boolean) => void
}) {
  const action = useAccountAction()
  return (
    <ConfirmDialog
      open={open}
      onOpenChange={onOpenChange}
      title={`Reset two-step verification for ${account.username}?`}
      description={
        <>
          <p>
            Their authenticator app stops working for RushDay and every session of the account ends.
          </p>
          <p>
            {account.role === 'Admin'
              ? 'They must set up two-step verification again before they can use the administrator pages.'
              : 'They can set it up again from their account page.'}
          </p>
        </>
      }
      confirmLabel="Reset two-step verification"
      onConfirm={async () => {
        await action.mutateAsync({ id: account.id, action: 'reset-mfa' })
        toast.success(`Two-step verification reset for ${account.username}.`)
      }}
    />
  )
}
