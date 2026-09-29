import { CalendarClock, CircleCheck, Clock, Pencil, UsersRound } from 'lucide-react'

import type { LecturerModuleSummary } from '@/api/types/lecturer'
import { ButtonLink, MarksStatusChip } from '@/components/ui'
import { cn } from '@/lib/cn'

/**
 * One module's grading state as an ordinal progress: draft, submitted, scheduled, published, plus a
 * "missing" count (05-frontend.md section 10, `/lecturer`). Never colour alone: each step carries its
 * icon and label, and the current step is the one with an accent ring.
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

  return (
    <div className="flex flex-col gap-3 py-4 first:pt-0 last:pb-0 sm:flex-row sm:items-center sm:justify-between sm:gap-6">
      <div className="min-w-0 space-y-2">
        <div className="flex flex-wrap items-center gap-2">
          <span className="font-mono text-sm font-semibold text-text">{module.code}</span>
          <span className="truncate text-sm text-muted">{module.title}</span>
          <MarksStatusChip
            status={marks.status}
            publishedAt={marks.publishedAt}
            {...(timeZone ? { timeZone } : {})}
          />
        </div>
        {marks.status === 'noStudents' ? (
          <p className="text-sm text-muted">No students enrolled this year.</p>
        ) : (
          <>
            <ol aria-label={`Grading progress for ${module.code}`} className="flex items-center gap-1">
              {STEPS.map((step, index) => {
                const Icon = step.icon
                const reached = index <= current
                return (
                  <li key={step.status} className="flex items-center gap-1">
                    <span
                      className={cn(
                        'flex items-center gap-1 rounded-full px-2 py-0.5 text-xs font-medium',
                        reached ? 'bg-primary-soft text-primary' : 'bg-surface-2 text-subtle',
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
            <p className="flex items-center gap-1.5 text-sm text-muted">
              <UsersRound aria-hidden="true" className="size-4" />
              {marks.entered} of {marks.total} entered
              {marks.missing > 0 && `, ${marks.missing} missing`}
            </p>
          </>
        )}
      </div>
      <ButtonLink
        to={`/lecturer/modules/${module.code}/marks`}
        variant="secondary"
        size="sm"
        className="shrink-0"
      >
        Enter marks
      </ButtonLink>
    </div>
  )
}
