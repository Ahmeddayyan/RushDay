import { useState, type ReactNode } from 'react'
import { zodResolver } from '@hookform/resolvers/zod'
import { useForm, useWatch } from 'react-hook-form'

import { describeProblem, isProblem, mapFieldErrors, type ProblemContext } from '@/api/problem'
import {
  AlertDialog,
  AlertDialogCancel,
  AlertDialogContent,
  AlertDialogFooter,
  Button,
  FormError,
  FormField,
  Textarea,
} from '@/components/ui'

import { REASON_MAX, REASON_MIN, reasonSchema, type ReasonFormValues } from '../lib/schemas'

export interface ReasonDialogProps {
  open: boolean
  onOpenChange: (open: boolean) => void
  title: ReactNode
  /** The consequence, stated before anything happens (05-frontend.md section 9.3). */
  description: ReactNode
  confirmLabel: string
  confirmVariant?: 'danger' | 'primary'
  reasonLabel?: string
  reasonHint?: string
  /** Runs the action; throw to keep the dialog open with the error shown. */
  onConfirm: (reason: string) => Promise<unknown>
  errorContext?: ProblemContext
  /** Extra content above the reason (a warning, a count). */
  children?: ReactNode
}

function ReasonForm({
  confirmLabel,
  confirmVariant,
  reasonLabel,
  reasonHint,
  onConfirm,
  errorContext,
  children,
  onDone,
}: Omit<ReasonDialogProps, 'open' | 'onOpenChange' | 'title' | 'description'> & {
  onDone: () => void
}) {
  const [formError, setFormError] = useState<string | null>(null)
  const {
    register,
    handleSubmit,
    setError,
    control,
    formState: { errors, isSubmitting },
  } = useForm<ReasonFormValues>({
    resolver: zodResolver(reasonSchema),
    mode: 'onBlur',
    reValidateMode: 'onChange',
    defaultValues: { reason: '' },
  })
  const reason = useWatch({ control, name: 'reason' })

  const submit = handleSubmit(async (values) => {
    setFormError(null)
    try {
      await onConfirm(values.reason)
      onDone()
    } catch (error) {
      if (isProblem(error, 'validation')) {
        const { fields, other } = mapFieldErrors(error, ['reason'] as const)
        if (fields.reason) setError('reason', { message: fields.reason }, { shouldFocus: true })
        setFormError(other.join(' ') || null)
      } else {
        setFormError(describeProblem(error, errorContext).message)
      }
    }
  })

  return (
    <form onSubmit={(event) => void submit(event)} noValidate className="flex flex-col gap-4">
      {children}
      <FormField
        label={reasonLabel ?? 'Reason'}
        required
        error={errors.reason?.message}
        hint={
          <>
            {reasonHint ?? 'Recorded in the audit log.'} {reason.trim().length} of {REASON_MAX}{' '}
            characters (at least {REASON_MIN}).
          </>
        }
      >
        <Textarea {...register('reason')} rows={3} maxLength={REASON_MAX} />
      </FormField>
      <FormError>{formError}</FormError>
      <AlertDialogFooter>
        <AlertDialogCancel>Cancel</AlertDialogCancel>
        <Button type="submit" variant={confirmVariant ?? 'danger'} loading={isSubmitting}>
          {confirmLabel}
        </Button>
      </AlertDialogFooter>
    </form>
  )
}

/**
 * An `AlertDialog` for an action that needs a reason (10 to 400 characters, recorded in the audit
 * log): mark as left, trim to capacity, unpublish, return to draft, override withdraw. The dialog
 * waits for the server: it closes on success and shows the error in place otherwise.
 */
export function ReasonDialog({
  open,
  onOpenChange,
  title,
  description,
  ...formProps
}: ReasonDialogProps) {
  return (
    <AlertDialog open={open} onOpenChange={onOpenChange}>
      <AlertDialogContent title={title} description={description}>
        <ReasonForm {...formProps} onDone={() => onOpenChange(false)} />
      </AlertDialogContent>
    </AlertDialog>
  )
}
