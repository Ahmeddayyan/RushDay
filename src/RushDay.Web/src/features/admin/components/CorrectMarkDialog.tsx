import { useState } from 'react'
import { zodResolver } from '@hookform/resolvers/zod'
import { TriangleAlert } from 'lucide-react'
import { useForm, useWatch } from 'react-hook-form'

import { describeProblem, isProblem, mapFieldErrors } from '@/api/problem'
import type { GradeOutcome } from '@/api/types/common'
import {
  AlertDialog,
  AlertDialogCancel,
  AlertDialogContent,
  AlertDialogFooter,
  Button,
  FormError,
  FormField,
  Input,
  Select,
  Textarea,
} from '@/components/ui'
import { toast } from '@/lib/toast'

import { useCorrectMark } from '../hooks/useResults'
import { describeMark } from '../lib/marks'
import {
  correctMarkSchema,
  REASON_MAX,
  REASON_MIN,
  type CorrectMarkFormValues,
} from '../lib/schemas'

export interface CorrectionTarget {
  code: string
  studentNumber: string
  studentName: string
  /** The grade as it stands: outcome and mark. */
  current: { outcome: GradeOutcome | null; mark: number | null }
  /** Students can see this grade now (Published and its instant has passed). */
  live: boolean
}

const OUTCOMES = [
  { value: 'mark', label: 'Mark' },
  { value: 'absent', label: 'Absent' },
  { value: 'deferred', label: 'Deferred' },
]

function CorrectMarkForm({ target, onDone }: { target: CorrectionTarget; onDone: () => void }) {
  const correct = useCorrectMark()
  const [formError, setFormError] = useState<string | null>(null)
  const {
    register,
    handleSubmit,
    setError,
    setValue,
    control,
    formState: { errors, isSubmitting },
  } = useForm<CorrectMarkFormValues>({
    resolver: zodResolver(correctMarkSchema),
    mode: 'onBlur',
    reValidateMode: 'onChange',
    defaultValues: {
      outcome: target.current.outcome ?? 'mark',
      mark: target.current.mark === null ? '' : String(target.current.mark),
      reason: '',
    },
  })
  const outcome = useWatch({ control, name: 'outcome' })
  const reason = useWatch({ control, name: 'reason' })
  const outcomeField = register('outcome')

  const submit = handleSubmit(async (values) => {
    setFormError(null)
    try {
      const result = await correct.mutateAsync({
        code: target.code,
        studentNumber: target.studentNumber,
        body: {
          outcome: values.outcome,
          mark: values.outcome === 'mark' ? Number(values.mark) : null,
          reason: values.reason,
        },
      })
      toast.success(
        `Corrected ${target.studentNumber} on ${target.code}: ${describeMark(result.before)} → ${describeMark(result.after)}.`,
      )
      onDone()
    } catch (error) {
      if (isProblem(error, 'validation')) {
        const { fields, other } = mapFieldErrors(error, ['outcome', 'mark', 'reason'] as const)
        if (fields.mark) setError('mark', { message: fields.mark })
        if (fields.reason) setError('reason', { message: fields.reason })
        if (fields.outcome) setError('outcome', { message: fields.outcome })
        setFormError(other.join(' ') || null)
      } else {
        setFormError(describeProblem(error, { code: target.code, audience: 'admin' }).message)
      }
    }
  })

  return (
    <form onSubmit={(event) => void submit(event)} noValidate className="flex flex-col gap-4">
      {target.live && (
        <p className="flex items-start gap-2 rounded-md border border-warning/30 bg-warning-soft px-3 py-2 text-sm font-medium text-warning">
          <TriangleAlert aria-hidden="true" className="mt-0.5 size-4 shrink-0" />
          The student sees the corrected mark immediately.
        </p>
      )}
      <div className="grid gap-4 sm:grid-cols-2">
        <FormField label="Outcome" required error={errors.outcome?.message}>
          <Select
            {...outcomeField}
            onChange={(event) => {
              void outcomeField.onChange(event)
              if (event.target.value !== 'mark') setValue('mark', '', { shouldValidate: false })
            }}
            options={OUTCOMES}
          />
        </FormField>
        <FormField
          label="Mark"
          required={outcome === 'mark'}
          error={errors.mark?.message}
          hint={outcome === 'mark' ? '0 to 100.' : 'Only a Mark outcome carries a number.'}
        >
          <Input
            {...register('mark')}
            type="text"
            inputMode="numeric"
            pattern="[0-9]*"
            maxLength={3}
            disabled={outcome !== 'mark'}
          />
        </FormField>
      </div>
      <FormField
        label="Reason"
        required
        error={errors.reason?.message}
        hint={`Recorded in the audit log with the old and new mark. ${reason.trim().length} of ${REASON_MAX} characters (at least ${REASON_MIN}).`}
      >
        <Textarea {...register('reason')} rows={3} maxLength={REASON_MAX} />
      </FormField>
      <FormError>{formError}</FormError>
      <AlertDialogFooter>
        <AlertDialogCancel>Cancel</AlertDialogCancel>
        <Button type="submit" variant="primary" loading={isSubmitting}>
          Correct mark
        </Button>
      </AlertDialogFooter>
    </form>
  )
}

/**
 * `CorrectMarkDialog` (05-frontend.md section 10): one submitted, scheduled or published mark,
 * corrected with a reason; a correction keeps the grade's status, so a live mark changes for the
 * student at once and is labelled "Amended".
 */
export function CorrectMarkDialog({
  target,
  onOpenChange,
}: {
  /** Null closes the dialog. */
  target: CorrectionTarget | null
  onOpenChange: (open: boolean) => void
}) {
  return (
    <AlertDialog open={target !== null} onOpenChange={onOpenChange}>
      {target && (
        <AlertDialogContent
          title={`Correct ${target.studentName}'s mark for ${target.code}`}
          description={
            <p>
              {target.studentNumber} currently has {describeMark(target.current)}. The correction is
              labelled Amended wherever the mark is shown.
            </p>
          }
        >
          <CorrectMarkForm target={target} onDone={() => onOpenChange(false)} />
        </AlertDialogContent>
      )}
    </AlertDialog>
  )
}
