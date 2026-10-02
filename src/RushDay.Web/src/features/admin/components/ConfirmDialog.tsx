import { useState, type ReactNode } from 'react'

import { describeProblem, type ProblemContext } from '@/api/problem'
import {
  AlertDialog,
  AlertDialogCancel,
  AlertDialogContent,
  AlertDialogFooter,
  Button,
  FormError,
} from '@/components/ui'

export interface ConfirmDialogProps {
  open: boolean
  onOpenChange: (open: boolean) => void
  title: ReactNode
  /** The consequence (05-frontend.md section 9.3). */
  description: ReactNode
  confirmLabel: string
  confirmVariant?: 'danger' | 'primary'
  /** Runs the action; throw to keep the dialog open with the error shown. */
  onConfirm: () => Promise<unknown>
  errorContext?: ProblemContext
  children?: ReactNode
}

function ConfirmBody({
  confirmLabel,
  confirmVariant,
  onConfirm,
  errorContext,
  children,
  onDone,
}: Omit<ConfirmDialogProps, 'open' | 'onOpenChange' | 'title' | 'description'> & {
  onDone: () => void
}) {
  const [pending, setPending] = useState(false)
  const [error, setError] = useState<string | null>(null)

  async function confirm() {
    setPending(true)
    setError(null)
    try {
      await onConfirm()
      onDone()
    } catch (caught) {
      setError(describeProblem(caught, errorContext).message)
    } finally {
      setPending(false)
    }
  }

  return (
    <div className="flex flex-col gap-4">
      {children}
      <FormError>{error}</FormError>
      <AlertDialogFooter>
        <AlertDialogCancel>Cancel</AlertDialogCancel>
        <Button
          variant={confirmVariant ?? 'danger'}
          loading={pending}
          onClick={() => void confirm()}
        >
          {confirmLabel}
        </Button>
      </AlertDialogFooter>
    </div>
  )
}

/**
 * A destructive or irreversible confirmation without a reason (delete a window, lock or disable an
 * account, cancel a scheduled publication). It waits for the server and keeps errors in place.
 */
export function ConfirmDialog({
  open,
  onOpenChange,
  title,
  description,
  ...bodyProps
}: ConfirmDialogProps) {
  return (
    <AlertDialog open={open} onOpenChange={onOpenChange}>
      <AlertDialogContent title={title} description={description}>
        <ConfirmBody {...bodyProps} onDone={() => onOpenChange(false)} />
      </AlertDialogContent>
    </AlertDialog>
  )
}
