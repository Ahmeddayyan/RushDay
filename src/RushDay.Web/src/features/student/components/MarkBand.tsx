import { CalendarClock, CircleX, UserX } from 'lucide-react'

import type { GradeOutcome } from '@/api/types/common'
import { cn } from '@/lib/cn'

import { markBand, outcomeLabel } from '../grades'

export interface MarkBandProps {
  outcome: GradeOutcome
  mark: number | null
  className?: string
}

/**
 * A mark's band in words (05-frontend.md section 10, `/student/results`): "First", "2:1", "2:2",
 * "Third", or "Fail" with an icon and the danger colour; an absence or deferral shows an icon and
 * the word ("Absent", "Deferred"), so colour is never the only signal.
 */
export function MarkBand({ outcome, mark, className }: MarkBandProps) {
  if (outcome !== 'mark' || mark === null) {
    const other = outcome === 'mark' ? 'deferred' : outcome
    const Icon = other === 'absent' ? UserX : CalendarClock
    return (
      <span className={cn('inline-flex items-center gap-1.5 font-medium text-muted', className)}>
        <Icon aria-hidden="true" className="size-4 shrink-0" />
        {outcomeLabel(other)}
      </span>
    )
  }

  const band = markBand(mark)
  if (band === 'Fail') {
    return (
      <span className={cn('inline-flex items-center gap-1.5 font-medium text-danger', className)}>
        <CircleX aria-hidden="true" className="size-4 shrink-0" />
        Fail
      </span>
    )
  }
  return <span className={cn('font-medium text-text', className)}>{band}</span>
}
