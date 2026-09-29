import { CalendarClock, CircleCheck, Clock, Pencil, UsersRound } from 'lucide-react'

import type { LecturerModuleSummary } from '@/api/types/lecturer'
import { ButtonLink, MarksStatusChip } from '@/components/ui'
import { cn } from '@/lib/cn'
import { formatDateTime } from '@/lib/format'

/**
 * One module's grading state as an ordinal progress: draft, submitted, scheduled, published, plus a
 * "missing" count (05-frontend.md section 10, `/lecturer`). Never colour alone: each step carries its
 * icon and label, and the current step is the one with an accent ring (so no separate status chip is
 * repeated beside the title).
 */
const STEPS = [
  { status: 'draft', label: 'Draft', icon: Pencil },
  { status: 'submitted', label: 'Submitted', icon: Clock },
  { status: 'scheduled', label: 'Scheduled', icon: CalendarClock },
  { status: 'published', label: 'Published', icon: CircleCheck },
] as const

function stepIndex(status: LecturerModuleSummary['marks']['status']): number {
  if (status === 'noStudents') return -1
  return STEPS.findIndex((step) => step.status === status)
}

export interface GradingProgressRowProps {
  module: LecturerModuleSummary
  timeZone?: string
}

export function GradingProgressRow({ module, timeZone }: GradingProgressRowProps) {
  const { marks } = module
  const current = stepIndex(marks.status)
  const currentLabel = STEPS[current]?.label
  const editable = marks.status === 'draft'
  const instant =
    marks.publishedAt && (marks.status === 'scheduled' || marks.status === 'published')
      ? `${marks.status === 'scheduled' ? 'Publishes' : 'Published'} ${formatDateTime(marks.publishedAt, timeZone)}`
      : null

  return (
    <div className="flex flex-col gap-3 py-4 first:pt-0 last:pb-0 sm:flex-row sm:items-center sm:justify-between sm:gap-6">
      <div className="min-w-0 space-y-2">
        <div className="flex flex-wrap items-center gap-x-2 gap-y-1">
          <span className="font-mono text-sm font-semibold text-text">{module.code}</span>
          <span className="text-sm text-muted">{module.title}</span>
          {marks.status === 'noStudents' && (
            <MarksStatusChip status={marks.status} publishedAt={marks.publishedAt} />
          )}
        </div>
        {marks.status === 'noStudents' ? (
          <p className="text-sm text-muted">No students enrolled this year.</p>
        ) : (
          <>
            <ol
              aria-label={`Grading progress for ${module.code}${currentLabel ? `: ${currentLabel}` : ''}`}
              className="flex flex-wrap items-center gap-1"
            >
              {STEPS.map((step, index) => {
                const Icon = step.icon
                const reached = index <= current
                return (
                  <li
                    key={step.status}
                    className="flex items-center gap-1"
                    aria-current={index === current ? 'step' : undefined}
                  >
                    <span
                      className={cn(
                        'flex items-center gap-1 rounded-full px-2 py-0.5 text-xs font-medium',
                        // --muted, not --subtle: --subtle on --surface-2 is 4.39:1 in dark mode, under the 4.5:1 text needs.
                        reached ? 'bg-primary-soft text-primary' : 'bg-surface-2 text-muted',
                        index === current && 'ring-2 ring-primary/40',
                      )}
                    >
                      <Icon aria-hidden="true" className="size-3.5" />
                      {step.label}
                    </span>
                    {index < STEPS.length - 1 && (
                      <span aria-hidden="true" className="h-px w-3 bg-border" />
                    )}
                  </li>
                )
              })}
            </ol>
            <p className="flex flex-wrap items-center gap-x-1.5 text-sm text-muted">
              <UsersRound aria-hidden="true" className="size-4" />
              <span>
                {marks.entered} of {marks.total} entered
                {marks.missing > 0 && `, ${marks.missing} missing`}
              </span>
              {instant && <span>· {instant}</span>}
            </p>
          </>
        )}
      </div>
      {marks.status !== 'noStudents' && (
        <ButtonLink
          to={`/lecturer/modules/${module.code}/marks`}
          variant={editable ? 'secondary' : 'ghost'}
          size="sm"
          className="shrink-0 self-start sm:self-center"
        >
          {editable ? 'Enter marks' : 'View marks'}
        </ButtonLink>
      )}
    </div>
  )
}
