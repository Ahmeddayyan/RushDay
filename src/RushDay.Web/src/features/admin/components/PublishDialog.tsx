import { useId, useState } from 'react'
import { GraduationCap } from 'lucide-react'

import { describeProblem } from '@/api/problem'
import type { AdminResultsModule, PublishResponse } from '@/api/types/admin'
import type { Semester } from '@/api/types/common'
import {
  AlertDialog,
  AlertDialogCancel,
  AlertDialogContent,
  AlertDialogFooter,
  Button,
  Checkbox,
  FormError,
} from '@/components/ui'
import { formatDateTime, formatNumber, formatSemester } from '@/lib/format'

import { usePublishResults } from '../hooks/useResults'
import { exclusionReason, publishPreview } from '../lib/results'
import {
  DAY_MS,
  nextNineAm,
  PUBLISH_MAX_DAYS,
  toApiInstant,
  toZonedInput,
  zonedInputToMs,
} from '../lib/zonedTime'

import { ZonedDateTimeField } from './ZonedDateTimeField'

export interface PublishDialogProps {
  open: boolean
  onOpenChange: (open: boolean) => void
  academicYear: string
  semester: Semester
  modules: AdminResultsModule[]
  timeZone: string
  /** The server's clock when the dialog opened: the picker's minimum and the 90-day maximum. */
  openedAt: number
  onPublished: (result: PublishResponse, now: boolean) => void
}

function plural(n: number, one: string, many: string): string {
  return `${formatNumber(n)} ${n === 1 ? one : many}`
}

/**
 * The results-day button (05-frontend.md section 10, `PublishDialog`): publish now or at an instant
 * up to 90 days ahead in the institution's zone, a preview of what is included and excluded, the
 * pinned announcement, and the exam-board confirmation that enables the primary button.
 */
function PublishForm({
  academicYear,
  semester,
  modules,
  timeZone,
  openedAt,
  onPublished,
}: Omit<PublishDialogProps, 'open' | 'onOpenChange'>) {
  const publish = usePublishResults()
  const preview = publishPreview(modules)
  const [publishNow, setPublishNow] = useState(true)
  const [when, setWhen] = useState(() => toZonedInput(nextNineAm(openedAt, timeZone), timeZone))
  const [announce, setAnnounce] = useState(true)
  const [approved, setApproved] = useState(false)
  const [whenError, setWhenError] = useState<string | null>(null)
  const [formError, setFormError] = useState<string | null>(null)
  const excludedId = useId()

  const min = toZonedInput(openedAt, timeZone)
  const max = toZonedInput(openedAt + PUBLISH_MAX_DAYS * DAY_MS, timeZone)
  const whenMs = zonedInputToMs(when, timeZone)
  const n = preview.modules.length
  const label = publishNow
    ? `Publish ${plural(n, 'module', 'modules')} now`
    : whenMs !== null
      ? `Schedule ${plural(n, 'module', 'modules')} for ${formatDateTime(whenMs, timeZone)}`
      : `Schedule ${plural(n, 'module', 'modules')}`

  function checkWhen(): number | null {
    if (publishNow) return null
    if (whenMs === null) {
      setWhenError('Enter the date and time students should see these marks.')
      return -1
    }
    if (whenMs <= openedAt) {
      setWhenError('Choose a time in the future, or switch on "Publish now".')
      return -1
    }
    if (whenMs > openedAt + PUBLISH_MAX_DAYS * DAY_MS) {
      setWhenError('Choose a date within the next 90 days.')
      return -1
    }
    setWhenError(null)
    return whenMs
  }

  async function submit() {
    setFormError(null)
    const scheduled = checkWhen()
    if (scheduled === -1) return
    try {
      const result = await publish.mutateAsync({
        academicYear,
        semester,
        // "Now" is sent as the dialog's opening instant; the server replaces a past instant by now.
        publishAt: toApiInstant(scheduled ?? openedAt),
        announce,
      })
      onPublished(result, scheduled === null)
    } catch (error) {
      setFormError(describeProblem(error, { semester, academicYear }).message)
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
      <div className="rounded-md border border-border bg-surface-2 px-4 py-3 text-sm">
        <p className="font-medium text-text">
          Will publish {plural(n, 'module', 'modules')} ({plural(preview.grades, 'mark', 'marks')});{' '}
          {plural(preview.excluded.length, 'module', 'modules')} excluded
        </p>
        {preview.excluded.length > 0 && (
          <>
            <p id={excludedId} className="mt-2 text-muted">
              Excluded, and why:
            </p>
            <ul aria-labelledby={excludedId} className="mt-1 max-h-40 overflow-y-auto">
              {preview.excluded.map((exclusion) => (
                <li key={exclusion.code} className="flex gap-2">
                  <span className="font-mono">{exclusion.code}</span>
                  <span className="text-muted">{exclusionReason(exclusion)}</span>
                </li>
              ))}
            </ul>
          </>
        )}
      </div>

      <Checkbox
        label="Publish now"
        hint="Switch off to schedule the results-day instant instead."
        checked={publishNow}
        onChange={(event) => {
          setPublishNow(event.target.checked)
          setWhenError(null)
        }}
      />
      {!publishNow && (
        <ZonedDateTimeField
          label="Students see the marks at"
          timeZone={timeZone}
          value={when}
          error={whenError ?? undefined}
          required
          hint="Within the next 90 days."
          inputProps={{
            value: when,
            min,
            max,
            onChange: (event) => {
              setWhen(event.target.value)
              setWhenError(null)
            },
          }}
        />
      )}
      <Checkbox
        label={`Post a pinned university announcement: ${formatSemester(semester)} ${academicYear} results are available`}
        checked={announce}
        onChange={(event) => setAnnounce(event.target.checked)}
      />
      <Checkbox
        label="The exam board has approved these marks"
        hint="Required before anything is published."
        required
        checked={approved}
        onChange={(event) => setApproved(event.target.checked)}
      />
      <p className="text-sm font-medium text-text">
        This is the results-day button. Students see these marks at the chosen instant.
      </p>
      <FormError>{formError}</FormError>
      <AlertDialogFooter>
        <AlertDialogCancel>Cancel</AlertDialogCancel>
        <Button type="submit" disabled={!approved || n === 0} loading={publish.isPending}>
          <GraduationCap aria-hidden="true" className="size-4" />
          {label}
        </Button>
      </AlertDialogFooter>
    </form>
  )
}

export function PublishDialog({ open, onOpenChange, ...formProps }: PublishDialogProps) {
  return (
    <AlertDialog open={open} onOpenChange={onOpenChange}>
      <AlertDialogContent
        title={`Publish ${formatSemester(formProps.semester)} ${formProps.academicYear} results`}
        description="Only modules whose leader has submitted every mark are published. Partly entered marks are never published."
      >
        <PublishForm {...formProps} />
      </AlertDialogContent>
    </AlertDialog>
  )
}
