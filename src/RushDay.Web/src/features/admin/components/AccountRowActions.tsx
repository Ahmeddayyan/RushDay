import { useId, useState } from 'react'
import {
  Ban,
  ChevronDown,
  CircleCheck,
  GraduationCap,
  KeyRound,
  Lock,
  LockOpen,
  ShieldOff,
} from 'lucide-react'
import { useNavigate } from 'react-router'

import { describeProblem } from '@/api/problem'
import type { AccountView } from '@/api/types/common'
import { useAuth } from '@/app/AuthProvider'
import {
  Button,
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuSeparator,
  DropdownMenuTrigger,
  Tooltip,
} from '@/components/ui'
import { toast } from '@/lib/toast'

import { useAccountAction, useResetPassword } from '../hooks/useAccounts'

import { ConfirmDialog } from './ConfirmDialog'
import { ResetMfaDialog } from './ResetMfaDialog'
import { TemporaryPasswordDialog } from './TemporaryPasswordDialog'

const DEMO_READ_ONLY = 'Demo accounts are read-only'

const LOCK_HINT =
  'Lock: a temporary block that keeps the account (for example after suspicious activity)'
const DISABLE_HINT = 'Disable: the person has left; sign-in stops within 5 minutes'

function ItemText({ label, hint }: { label: string; hint?: string }) {
  return (
    <span className="flex min-w-0 flex-col">
      <span>{label}</span>
      {hint && <span className="max-w-64 text-xs text-muted">{hint}</span>}
    </span>
  )
}

type DialogKind = 'reset' | 'lock' | 'disable' | 'mfa' | null

/**
 * The row actions of `/admin/accounts` and the student support page's account card (05-frontend.md
 * section 10): Reset password, Lock / Unlock, Disable / Enable, Reset two-step verification, View
 * student. A demo account's actions are disabled with the tooltip "Demo accounts are read-only".
 */
export function AccountRowActions({
  account,
  showViewStudent = true,
}: {
  account: AccountView
  showViewStudent?: boolean
}) {
  const { user, logout } = useAuth()
  const navigate = useNavigate()
  const action = useAccountAction()
  const resetPassword = useResetPassword()
  const [dialog, setDialog] = useState<DialogKind>(null)
  const [credentials, setCredentials] = useState<{ username: string; password: string } | null>(
    null,
  )
  const hintId = useId()
  const self = user?.id === account.id
  const label = `Actions for ${account.username}`

  if (account.isDemo) {
    return (
      <>
        <Tooltip content={DEMO_READ_ONLY}>
          <Button
            variant="secondary"
            size="sm"
            aria-label={label}
            aria-disabled="true"
            aria-describedby={hintId}
            onClick={(event) => event.preventDefault()}
          >
            Actions
            <ChevronDown aria-hidden="true" className="size-4" />
          </Button>
        </Tooltip>
        <span id={hintId} hidden>
          {DEMO_READ_ONLY}
        </span>
      </>
    )
  }

  async function run(kind: 'unlock' | 'enable') {
    try {
      await action.mutateAsync({ id: account.id, action: kind })
      toast.success(
        kind === 'unlock'
          ? `Unlocked ${account.username}. They can sign in again.`
          : `Enabled ${account.username}. They can sign in again.`,
      )
    } catch (error) {
      const number = account.studentNumber ?? account.staffNumber
      toast.error(describeProblem(error, number ? { number } : {}).message)
    }
  }

  const close = (open: boolean) => {
    if (!open) setDialog(null)
  }

  return (
    <>
      <DropdownMenu modal={false}>
        <DropdownMenuTrigger asChild>
          <Button variant="secondary" size="sm" aria-label={label}>
            Actions
            <ChevronDown aria-hidden="true" className="size-4" />
          </Button>
        </DropdownMenuTrigger>
        <DropdownMenuContent>
          <DropdownMenuItem onSelect={() => setDialog('reset')}>
            <KeyRound aria-hidden="true" />
            <ItemText label="Reset password" />
          </DropdownMenuItem>
          {account.state === 'locked' ? (
            <DropdownMenuItem onSelect={() => void run('unlock')}>
              <LockOpen aria-hidden="true" />
              <ItemText label="Unlock" />
            </DropdownMenuItem>
          ) : (
            <DropdownMenuItem onSelect={() => setDialog('lock')}>
              <Lock aria-hidden="true" />
              <ItemText label="Lock" hint={LOCK_HINT} />
            </DropdownMenuItem>
          )}
          {account.state === 'disabled' ? (
            <DropdownMenuItem onSelect={() => void run('enable')}>
              <CircleCheck aria-hidden="true" />
              <ItemText label="Enable" />
            </DropdownMenuItem>
          ) : (
            <DropdownMenuItem tone="danger" onSelect={() => setDialog('disable')}>
              <Ban aria-hidden="true" />
              <ItemText label="Disable" hint={DISABLE_HINT} />
            </DropdownMenuItem>
          )}
          {account.mfaEnabled && (
            <DropdownMenuItem onSelect={() => setDialog('mfa')}>
              <ShieldOff aria-hidden="true" />
              <ItemText label="Reset two-step verification" />
            </DropdownMenuItem>
          )}
          {showViewStudent && account.studentNumber && (
            <>
              <DropdownMenuSeparator />
              <DropdownMenuItem
                onSelect={() => void navigate(`/admin/students/${account.studentNumber}`)}
              >
                <GraduationCap aria-hidden="true" />
                <ItemText label="View student" />
              </DropdownMenuItem>
            </>
          )}
        </DropdownMenuContent>
      </DropdownMenu>

      <ConfirmDialog
        open={dialog === 'reset'}
        onOpenChange={close}
        title={`Reset the password for ${account.username}?`}
        description={
          <>
            <p>
              Their current password stops working, every session ends, and they must choose a new
              password at their next sign-in.
            </p>
            {self && <p>You&apos;ll be signed out after copying the temporary password.</p>}
          </>
        }
        confirmLabel="Reset password"
        onConfirm={async () => {
          const result = await resetPassword.mutateAsync(account.id)
          setCredentials({ username: account.username, password: result.temporaryPassword })
        }}
      />
      <ConfirmDialog
        open={dialog === 'lock'}
        onOpenChange={close}
        title={`Lock ${account.username}?`}
        description={`${LOCK_HINT}. They can't sign in, and every session ends, until an administrator unlocks the account.`}
        confirmLabel="Lock account"
        onConfirm={async () => {
          await action.mutateAsync({ id: account.id, action: 'lock' })
          toast.success(`Locked ${account.username}.`)
        }}
      />
      <ConfirmDialog
        open={dialog === 'disable'}
        onOpenChange={close}
        title={`Disable ${account.username}?`}
        description={`${DISABLE_HINT}. Every session of the account ends. Enable it again if this was a mistake.`}
        confirmLabel="Disable account"
        onConfirm={async () => {
          await action.mutateAsync({ id: account.id, action: 'disable' })
          toast.success(`Disabled ${account.username}.`)
        }}
      />
      <ResetMfaDialog account={account} open={dialog === 'mfa'} onOpenChange={close} />
      <TemporaryPasswordDialog
        credentials={credentials}
        selfReset={self}
        onClose={() => {
          setCredentials(null)
          // The reset rotated this session's security stamp: sign out now rather than at the next
          // request. A failure here means the session has already ended, which is the goal.
          if (self) logout().catch(() => undefined)
        }}
      />
    </>
  )
}
