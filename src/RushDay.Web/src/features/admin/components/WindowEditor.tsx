import { useId, useState } from 'react'
import { CalendarCheck, CalendarX, Trash2 } from 'lucide-react'

import { describeProblem } from '@/api/problem'
import type { Semester, WindowInfo } from '@/api/types/common'
import {
  Button,
  Card,
  CardHeader,
  CardTitle,
  FormError,
  FormField,
  Input,
  Select,
} from '@/components/ui'
import { formatSemester } from '@/lib/format'
import { toast } from '@/lib/toast'

import { useCreateWindow, useDeleteWindow, useUpdateWindow } from '../hooks/useWindows'
import { isAcademicYear } from '../lib/schemas'
import {
  closeNow,
  openNowFor7Days,
  validateWindow,
  type WindowDraft,
  type WindowErrors,
} from '../lib/windows'
import { toZonedInput } from '@/lib/zonedTime'

import { ConfirmDialog } from './ConfirmDialog'
import { WindowStateChip } from './StatusChips'
import { ZonedDateTimeField } from '@/components/ui/ZonedDateTimeField'

export interface WindowEditorProps {
  /** Null for a window being added. */
  window: WindowInfo | null
  timeZone: string
  /** Milliseconds on the server clock. */
  now: () => number
  /** For a new window: the academic year it starts with. */
  defaultAcademicYear?: string
  /** A new window was saved or discarded. */
  onDone?: () => void
}

function draftOf(window: WindowInfo | null, timeZone: string): WindowDraft {
  return {
    opensAt: window ? toZonedInput(window.opensAt, timeZone) : '',
    closesAt: window ? toZonedInput(window.closesAt, timeZone) : '',
    withdrawalDeadlineAt: window ? toZonedInput(window.withdrawalDeadlineAt, timeZone) : '',
  }
}

/**
 * One enrolment window (05-frontend.md section 10, `/admin/enrolment`): opens, closes and the
 * withdrawal deadline as wall-clock times in the institution's zone, the state chip, Save and
 * Delete, and the shortcuts "Open now for 7 days" and "Close now".
 */
export function WindowEditor({
  window,
  timeZone,
  now,
  defaultAcademicYear = '',
  onDone,
}: WindowEditorProps) {
  const create = useCreateWindow()
  const update = useUpdateWindow()
  const remove = useDeleteWindow()
  const headingId = useId()
  const [draft, setDraft] = useState<WindowDraft>(() => draftOf(window, timeZone))
  const [academicYear, setAcademicYear] = useState(window?.academicYear ?? defaultAcademicYear)
  const [semester, setSemester] = useState<Semester>(window?.semester ?? 'autumn')
  const [errors, setErrors] = useState<WindowErrors & { academicYear?: string }>({})
  const [formError, setFormError] = useState<string | null>(null)
  const [dialog, setDialog] = useState<'delete' | 'close' | null>(null)
  const isNew = window === null
  const dirty = isNew || JSON.stringify(draft) !== JSON.stringify(draftOf(window, timeZone))
  const title = isNew ? 'New window' : `${window.academicYear} ${formatSemester(window.semester)}`

  function setField(field: keyof WindowDraft, value: string) {
    setDraft((current) => ({ ...current, [field]: value }))
    setErrors((current) => {
      const next = { ...current }
      delete next[field]
      return next
    })
  }

  async function send(instants: WindowDraft, success: string) {
    setFormError(null)
    try {
      if (window) {
        const saved = await update.mutateAsync({ id: window.id, body: instants })
        setDraft(draftOf(saved, timeZone))
      } else {
        await create.mutateAsync({ academicYear, semester, ...instants })
        onDone?.()
      }
      toast.success(success)
      return true
    } catch (error) {
      // `window-exists` names the year and semester; `window-dates-invalid` states the rule.
      setFormError(describeProblem(error, window ? {} : { academicYear, semester }).message)
      return false
    }
  }

  async function save() {
    const { errors: found, instants } = validateWindow(draft, timeZone)
    const yearError =
      isNew && !isAcademicYear(academicYear.trim())
        ? 'Write the academic year like 2026/27.'
        : undefined
    setErrors({ ...found, ...(yearError ? { academicYear: yearError } : {}) })
    if (!instants || yearError) return
    await send(
      instants,
      `Saved the ${isNew ? `${academicYear} ${formatSemester(semester)}` : title} window.`,
    )
  }

  const saving = create.isPending || update.isPending

  return (
    <Card>
      <section aria-labelledby={headingId} className="flex flex-col gap-4">
        <CardHeader
          className="mb-0"
          actions={window ? <WindowStateChip state={window.state} /> : undefined}
        >
          <CardTitle id={headingId}>{title}</CardTitle>
        </CardHeader>

        {isNew && (
          <div className="grid gap-4 sm:grid-cols-2">
            <FormField label="Academic year" required error={errors.academicYear}>
              <Input
                value={academicYear}
                onChange={(event) => setAcademicYear(event.target.value)}
                placeholder="2026/27"
                maxLength={7}
              />
            </FormField>
            <FormField label="Semester" required>
              <Select
                value={semester}
                onChange={(event) => setSemester(event.target.value as Semester)}
                options={[
                  { value: 'autumn', label: 'Autumn' },
                  { value: 'spring', label: 'Spring' },
                ]}
              />
            </FormField>
          </div>
        )}

        <div className="grid gap-4 md:grid-cols-3">
          <ZonedDateTimeField
            label="Opens"
            timeZone={timeZone}
            value={draft.opensAt}
            error={errors.opensAt}
            required
            inputProps={{
              value: draft.opensAt,
              onChange: (event) => setField('opensAt', event.target.value),
            }}
          />
          <ZonedDateTimeField
            label="Closes"
            timeZone={timeZone}
            value={draft.closesAt}
            error={errors.closesAt}
            required
            inputProps={{
              value: draft.closesAt,
              onChange: (event) => setField('closesAt', event.target.value),
            }}
          />
          <ZonedDateTimeField
            label="Withdrawal deadline"
            timeZone={timeZone}
            value={draft.withdrawalDeadlineAt}
            error={errors.withdrawalDeadlineAt}
            required
            hint="Not before closes."
            inputProps={{
              value: draft.withdrawalDeadlineAt,
              onChange: (event) => setField('withdrawalDeadlineAt', event.target.value),
            }}
          />
        </div>

        <FormError>{formError}</FormError>

        <div className="flex flex-wrap items-center gap-2">
          <Button onClick={() => void save()} loading={saving} disabled={!dirty}>
            {isNew ? 'Add window' : 'Save'}
          </Button>
          {window && window.state !== 'open' && (
            <Button
              variant="secondary"
              onClick={() =>
                void send(openNowFor7Days(window, now()), `${title} is open for 7 days.`)
              }
              disabled={saving}
            >
              <CalendarCheck aria-hidden="true" className="size-4" />
              Open now for 7 days
            </Button>
          )}
          {window && window.state === 'open' && (
            <Button variant="secondary" onClick={() => setDialog('close')} disabled={saving}>
              <CalendarX aria-hidden="true" className="size-4" />
              Close now
            </Button>
          )}
          {window ? (
            <Button
              variant="ghost"
              className="text-danger sm:ml-auto"
              onClick={() => setDialog('delete')}
            >
              <Trash2 aria-hidden="true" className="size-4" />
              Delete
            </Button>
          ) : (
            <Button variant="ghost" onClick={onDone}>
              Discard
            </Button>
          )}
        </div>
      </section>

      {window && (
        <>
          <ConfirmDialog
            open={dialog === 'close'}
            onOpenChange={(open) => !open && setDialog(null)}
            title={`Close ${title} now?`}
            description="Students who are enrolling right now will see Enrolment closed. Continue?"
            confirmLabel="Close now"
            onConfirm={async () => {
              const instants = closeNow(window, now())
              const saved = await update.mutateAsync({ id: window.id, body: instants })
              setDraft(draftOf(saved, timeZone))
              toast.success(`${title} is closed.`)
            }}
          />
          <ConfirmDialog
            open={dialog === 'delete'}
            onOpenChange={(open) => !open && setDialog(null)}
            title={`Delete the ${title} window?`}
            description={`Students can't enrol themselves on ${formatSemester(window.semester)} modules for ${window.academicYear} until a new window is added. Existing enrolments stay.`}
            confirmLabel="Delete window"
            onConfirm={async () => {
              await remove.mutateAsync(window.id)
              toast.success(`Deleted the ${title} window.`)
            }}
          />
        </>
      )}
    </Card>
  )
}
