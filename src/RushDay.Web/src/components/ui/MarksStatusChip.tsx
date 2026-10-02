import { CalendarClock, CircleCheck, Clock, PenLine, Pencil, UsersRound } from 'lucide-react'

import type { MarksStatusValue } from '@/api/types/common'
import { formatDateTime, formatShortDate } from '@/lib/format'

import { Badge, type BadgeVariant } from './Badge'

export interface MarksStatusChipProps {
  status: MarksStatusValue
  /** The publication's instant, for "Scheduled for {instant}". */
  publishedAt?: string | null
  timeZone?: string
  className?: string
}

const chip: Record<
  MarksStatusValue,
  { label: string; variant: BadgeVariant; icon: typeof Pencil }
> = {
  noStudents: { label: 'No students', variant: 'neutral', icon: UsersRound },
  draft: { label: 'Draft', variant: 'neutral', icon: Pencil },
  submitted: { label: 'Submitted', variant: 'info', icon: Clock },
  scheduled: { label: 'Scheduled', variant: 'warning', icon: CalendarClock },
  published: { label: 'Published', variant: 'success', icon: CircleCheck },
}

/**
 * A module's marks state (05-frontend.md sections 9.3 and 10): Draft with a pencil, Submitted with a
 * clock, Scheduled with a calendar, Published with a check; never colour alone. Shared by the
 * lecturer and admin areas, so it lives here.
 */
export function MarksStatusChip({
  status,
  publishedAt,
  timeZone,
  className,
}: MarksStatusChipProps) {
  const { label, variant, icon } = chip[status]
  const text =
    status === 'scheduled' && publishedAt
      ? `Scheduled for ${formatDateTime(publishedAt, timeZone)}`
      : label
  return (
    <Badge variant={variant} icon={icon} {...(className ? { className } : {})}>
      {text}
    </Badge>
  )
}

/** "Amended {date}": a mark an administrator corrected after submission. */
export function AmendedBadge({
  correctedAt,
  timeZone,
}: {
  correctedAt: string
  timeZone?: string
}) {
  return (
    <Badge variant="info" icon={PenLine}>
      Amended {formatShortDate(correctedAt, timeZone)}
    </Badge>
  )
}
