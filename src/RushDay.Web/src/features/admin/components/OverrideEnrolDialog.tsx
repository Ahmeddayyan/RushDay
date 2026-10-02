import { useState } from 'react'
import { TriangleAlert } from 'lucide-react'

import { describeProblem, isProblem } from '@/api/problem'
import type { AdminModule } from '@/api/types/admin'
import {
  Button,
  Checkbox,
  Combobox,
  Dialog,
  DialogClose,
  DialogContent,
  DialogFooter,
  FormError,
  FormField,
  Textarea,
} from '@/components/ui'
import { formatNumber, formatSemester } from '@/lib/format'
import { toast } from '@/lib/toast'

import { useAdminModules } from '../hooks/useModules'
import { useOverrideEnrol } from '../hooks/useStudents'
import { REASON_MAX, REASON_MIN, reasonField } from '../lib/schemas'

const LOCKED = new Set(['submitted', 'scheduled', 'published'])

function OverrideEnrolForm({
  studentNumber,
  studentName,
  onDone,
}: {
  studentNumber: string
  studentName: string
  onDone: () => void
}) {
  const modules = useAdminModules(false)
  const enrol = useOverrideEnrol(studentNumber)
  const [text, setText] = useState('')
  const [module, setModule] = useState<AdminModule | null>(null)
  const [reason, setReason] = useState('')
  const [forceCapacity, setForceCapacity] = useState(false)
  const [errors, setErrors] = useState<{
    module?: string | undefined
    reason?: string | undefined
  }>({})
  const [formError, setFormError] = useState<string | null>(null)

  const needle = text.trim().toLowerCase()
  const items = (modules.data ?? [])
    .filter(
      (item) =>
        needle === '' ||
        item.code.toLowerCase().includes(needle) ||
        item.title.toLowerCase().includes(needle),
    )
    .slice(0, 20)
  const locked = module !== null && LOCKED.has(module.marks.status)
  const full = module !== null && module.placesRemaining === 0

  async function submit() {
    setFormError(null)
    const parsed = reasonField.safeParse(reason)
    const next: typeof errors = {}
    if (!module) next.module = 'Choose the module to enrol them on.'
    if (!parsed.success) next.reason = parsed.error.issues[0]?.message ?? 'Give a reason.'
    setErrors(next)
    if (!module || !parsed.success) return
    try {
      const result = await enrol.mutateAsync({
        moduleCode: module.code,
        reason: parsed.data,
        ...(forceCapacity ? { forceCapacity: true } : {}),
      })
      toast.success(
        `Enrolled ${studentNumber} on ${result.moduleCode}. ${formatNumber(result.placesRemaining)} places left.${
          result.capacityRaised ? ' Capacity raised by one.' : ''
        }`,
      )
      onDone()
    } catch (error) {
      const message = describeProblem(error, {
        code: module.code,
        audience: 'admin',
        enrolment: true,
      }).message
      setFormError(
        isProblem(error, 'module-full')
          ? `${message} Tick "Raise capacity by one if full" to enrol them anyway.`
          : message,
      )
    }
  }

  return (
    <form
      noValidate
      className="flex flex-col gap-4"
      onSubmit={(event) => {
        event.preventDefault()
        void submit()
      }}
    >
      <FormField
        label="Module"
        required
        error={errors.module}
        hint="Enrolment windows and the credit limit don't apply to an override."
      >
        <Combobox
          items={items}
          getKey={(item) => item.code}
          getLabel={(item) => `${item.code} ${item.title}`}
          renderItem={(item) => (
            <span className="flex flex-col">
              <span>
                <span className="font-mono">{item.code}</span> {item.title}
              </span>
              <span className="text-xs text-muted">
                {formatSemester(item.semester)} · {item.credits} credits ·{' '}
                {formatNumber(item.placesRemaining)} of {formatNumber(item.capacity)} places left
              </span>
            </span>
          )}
          value={module}
          onChange={(item) => {
            setModule(item)
            setErrors((current) => ({ ...current, module: undefined }))
          }}
          inputValue={text}
          onInputChange={setText}
          loading={modules.isPending}
          placeholder="Module code or title"
          emptyState={`No running modules match "${text.trim()}".`}
        />
      </FormField>
      {locked && (
        <p className="flex items-start gap-2 rounded-md border border-warning/30 bg-warning-soft px-3 py-2 text-sm font-medium text-warning">
          <TriangleAlert aria-hidden="true" className="mt-0.5 size-4 shrink-0" />
          Marks for {module.code} are already submitted. The lecturer will need the module returned
          to draft to enter a mark for this student.
        </p>
      )}
      <Checkbox
        label="Raise capacity by one if full"
        hint={
          full
            ? `${module.code} is full: capacity goes from ${formatNumber(module.capacity)} to ${formatNumber(module.capacity + 1)}.`
            : 'Only used when the module is full at the moment of enrolling.'
        }
        checked={forceCapacity}
        onChange={(event) => setForceCapacity(event.target.checked)}
      />
      <FormField
        label="Reason"
        required
        error={errors.reason}
        hint={`Recorded in the audit log. ${reason.trim().length} of ${REASON_MAX} characters (at least ${REASON_MIN}).`}
      >
        <Textarea
          value={reason}
          onChange={(event) => setReason(event.target.value)}
          rows={3}
          maxLength={REASON_MAX}
        />
      </FormField>
      <FormError>{formError}</FormError>
      <DialogFooter>
        <DialogClose asChild>
          <Button variant="secondary">Cancel</Button>
        </DialogClose>
        <Button type="submit" loading={enrol.isPending}>
          Enrol {studentName.split(' ')[0]}
        </Button>
      </DialogFooter>
    </form>
  )
}

/**
 * `OverrideEnrolDialog` (05-frontend.md section 10): enrol a student outside the window and the
 * credit limit, with a reason; capacity still applies unless "Raise capacity by one if full".
 */
export function OverrideEnrolDialog({
  studentNumber,
  studentName,
  open,
  onOpenChange,
}: {
  studentNumber: string
  studentName: string
  open: boolean
  onOpenChange: (open: boolean) => void
}) {
  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent
        title={`Enrol ${studentName} on a module`}
        description={`An administrator override for ${studentNumber}, recorded in the audit log.`}
      >
        <OverrideEnrolForm
          studentNumber={studentNumber}
          studentName={studentName}
          onDone={() => onOpenChange(false)}
        />
      </DialogContent>
    </Dialog>
  )
}
