import { ArrowRight, CalendarDays } from 'lucide-react'
import { Link } from 'react-router'

import type { Semester, TimetableEntry } from '@/api/types/common'
import { Card, CardHeader, CardTitle, EmptyState } from '@/components/ui'
import { formatSemester } from '@/lib/format'

import { timeRange, type ZonedMoment } from '../time'
import { entryKey, KIND_LABEL, nextClasses } from '../timetable'

export interface NextClassesCardProps {
  timetable: readonly TimetableEntry[]
  currentSemester: Semester
  /** The institution's wall clock now (`useZonedNow`). */
  now: ZonedMoment
}

/**
 * "Next classes" (05-frontend.md section 10, `/student`): today's remaining classes, else the next
 * day with classes, as "{code} {title} · {kind} · {room}" with times; a link to the full timetable
 * and the caption "{Semester} timetable".
 */
export function NextClassesCard({ timetable, currentSemester, now }: NextClassesCardProps) {
  const next = nextClasses(timetable, now)

  return (
    <Card>
      <CardHeader
        actions={
          <Link
            to="/student/timetable"
            className="inline-flex items-center gap-1 rounded-sm text-sm font-medium text-primary hover:underline"
          >
            Full timetable
            <ArrowRight aria-hidden="true" className="size-4" />
          </Link>
        }
      >
        <CardTitle>Next classes</CardTitle>
        <p className="text-sm text-muted">{formatSemester(currentSemester)} timetable</p>
      </CardHeader>
      {next === null ? (
        <EmptyState
          compact
          icon={CalendarDays}
          headingLevel="p"
          title="No classes this semester."
          description="Enrol on modules to build your timetable."
        />
      ) : (
        <div>
          <h3 className="mb-2 text-sm font-semibold text-muted">{next.label}</h3>
          <ul className="flex flex-col gap-2">
            {next.entries.map((entry) => (
              <li
                key={entryKey(entry)}
                className="flex gap-3 rounded-md border border-border bg-surface-2 px-3 py-2.5"
              >
                <span className="w-24 shrink-0 text-sm font-medium text-text tabular-nums">
                  {timeRange(entry.startTime, entry.endTime)}
                </span>
                <span className="min-w-0 text-sm text-text">
                  <span className="font-mono">{entry.moduleCode}</span> {entry.moduleTitle}
                  <span className="text-muted">
                    {' '}
                    · {KIND_LABEL[entry.kind]} · {entry.room}
                  </span>
                </span>
              </li>
            ))}
          </ul>
        </div>
      )}
    </Card>
  )
}
