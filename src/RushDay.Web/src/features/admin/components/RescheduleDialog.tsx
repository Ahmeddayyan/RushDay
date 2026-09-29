import { useState } from 'react'

import { describeProblem } from '@/api/problem'
import type { PublicationInfo } from '@/api/types/common'
import {
  Button,
  Dialog,
  DialogClose,
  DialogContent,
  DialogFooter,
  FormError,
} from '@/components/ui'
import { formatDateTime, formatSemester } from '@/lib/format'
import { toast } from '@/lib/toast'

import { useReschedulePublication } from '../hooks/useResults'
import {
  DAY_MS,
  PUBLISH_MAX_DAYS,
  toApiInstant,
  toZonedInput,
  zonedInputToMs,
} from '@/lib/zonedTime'

import { ZonedDateTimeField } from '@/components/ui/ZonedDateTimeField'

function RescheduleForm({
  publication,
  timeZone,
  openedAt,
  onDone,
}: {
  publication: PublicationInfo
  timeZone: string
  openedAt: number
  onDone: () => void
}) {
  const reschedule = useReschedulePublication()
  const [when, setWhen] = useState(() => toZonedInput(publication.publishAt, timeZone))
  const [error, setError] = useState<string | null>(null)
  const [formError, setFormError] = useState<string | null>(null)

  async function submit() {
    setFormError(null)
    const ms = zonedInputToMs(when, timeZone)
    if (ms === null) return setError('Enter the new date and time.')
    if (ms <= openedAt) return setError('Choose a time in the future.')
    if (ms > openedAt + PUBLISH_MAX_DAYS * DAY_MS) {
      return setError('Choose a date within the next 90 days.')
    }
    setError(null)
    try {
      await reschedule.mutateAsync({ id: publication.id, publishAt: toApiInstant(ms) })
      toast.success(
        `${formatSemester(publication.semester)} ${publication.academicYear} results now publish ${formatDateTime(ms, timeZone)}.`,
      )
      onDone()
    } catch (caught) {
      setFormError(describeProblem(caught).message)
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
      <ZonedDateTimeField
        label="Students see the marks at"
        timeZone={timeZone}
        value={when}
        error={error ?? undefined}
        required
        hint="Within the next 90 days."
        inputProps={{
          value: when,
          min: toZonedInput(openedAt, timeZone),
          max: toZonedInput(openedAt + PUBLISH_MAX_DAYS * DAY_MS, timeZone),
          onChange: (event) => {
            setWhen(event.target.value)
            setError(null)
          },
        }}
      />
      <FormError>{formError}</FormError>
      <DialogFooter>
        <DialogClose asChild>
          <Button variant="secondary">Cancel</Button>
        </DialogClose>
        <Button type="submit" loading={reschedule.isPending}>
          Reschedule
        </Button>
      </DialogFooter>
    </form>
  )
}

/** `RescheduleDialog`: moves a scheduled publication's instant (still at most 90 days ahead). */
export function RescheduleDialog({
  publication,
  timeZone,
  openedAt,
  onOpenChange,
}: {
  /** Null closes the dialog. */
  publication: PublicationInfo | null
  timeZone: string
  openedAt: number
  onOpenChange: (open: boolean) => void
}) {
  return (
    <Dialog open={publication !== null} onOpenChange={onOpenChange}>
      {publication && (
        <DialogContent
          title={`Reschedule ${formatSemester(publication.semester)} ${publication.academicYear} results`}
          description={`Currently ${formatDateTime(publication.publishAt, timeZone)}. Students see nothing until the new instant.`}
        >
          <RescheduleForm
            publication={publication}
            timeZone={timeZone}
            openedAt={openedAt}
            onDone={() => onOpenChange(false)}
          />
        </DialogContent>
      )}
    </Dialog>
  )
}
