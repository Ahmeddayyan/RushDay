import { useId } from 'react'
import { CalendarDays, CalendarPlus } from 'lucide-react'

import { useServerClock } from '@/api/endpoints/public'
import {
  Button,
  ButtonLink,
  Card,
  EmptyState,
  ErrorState,
  LoadingRegion,
  Refetching,
  Skeleton,
} from '@/components/ui'
import { downloadText } from '@/lib/download'
import { formatSemester } from '@/lib/format'
import { toast } from '@/lib/toast'
import { useDocumentTitle } from '@/lib/useDocumentTitle'

import { TimetableAgenda } from './components/TimetableAgenda'
import { TimetableGrid } from './components/TimetableGrid'
import { useTimetable } from './hooks/useTimetable'
import { buildTimetableIcs, icsFileName } from './ics'
import { useZonedNow } from './time'

function TimetableSkeleton() {
  return (
    <LoadingRegion label="your timetable">
      <div className="rounded-lg border border-border bg-surface p-4">
        <div className="mb-4 grid grid-cols-5 gap-3">
          {[0, 1, 2, 3, 4].map((key) => (
            <Skeleton key={key} className="h-6" />
          ))}
        </div>
        <div className="grid grid-cols-5 gap-3">
          {[0, 1, 2, 3, 4].map((key) => (
            <Skeleton key={key} className="h-48" />
          ))}
        </div>
      </div>
    </LoadingRegion>
  )
}

/**
 * `/student/timetable` (05-frontend.md section 10): the current semester's week as a table from
 * 768 px (with the agenda as its screen-reader twin) and as day tabs below, plus "Add to calendar
 * (.ics)". Data: `['student','timetable']`; the semester and year come from `['public','status']`.
 */
export function Component() {
  useDocumentTitle('Timetable · RushDay')
  const headingId = useId()
  const query = useTimetable()
  const { offsetMs, timeZone, status } = useServerClock()
  const now = useZonedNow(timeZone, offsetMs)

  const caption = status
    ? `${formatSemester(status.currentSemester)} ${status.academicYear} timetable`
    : 'This semester’s timetable'
  const entries = query.data ?? []

  const exportIcs = () => {
    if (!status) return
    const text = buildTimetableIcs(entries, {
      now: Date.now() + offsetMs,
      timeZone,
      calendarName: caption,
      semester: status.currentSemester,
      academicYear: status.academicYear,
    })
    downloadText(
      text,
      icsFileName(status.currentSemester, status.academicYear),
      'text/calendar;charset=utf-8',
    )
    toast.success('Your timetable file is downloading.')
  }

  let content
  if (query.isPending) {
    content = <TimetableSkeleton />
  } else if (query.isError) {
    content = (
      <Card>
        <ErrorState error={query.error} onRetry={() => void query.refetch()} />
      </Card>
    )
  } else if (entries.length === 0) {
    content = (
      <Card>
        <EmptyState
          icon={CalendarDays}
          title="No classes this semester."
          description="Enrol on modules to build your timetable."
          action={
            <ButtonLink to="/student/modules" variant="secondary">
              Browse modules
            </ButtonLink>
          }
        />
      </Card>
    )
  } else {
    content = (
      <Refetching active={query.isFetching}>
        <div className="max-md:hidden">
          <TimetableGrid
            entries={entries}
            labelledBy={headingId}
            caption={caption}
            today={now.weekday}
            nowMinutes={now.minutes}
          />
          <div className="sr-only">
            <TimetableAgenda entries={entries} today={now.weekday} mode="list" />
          </div>
        </div>
        <div className="md:hidden">
          <TimetableAgenda entries={entries} today={now.weekday} mode="tabs" />
        </div>
      </Refetching>
    )
  }

  return (
    <>
      {/* The heading is written out (not <PageHeader>) because the grid table is labelled by its id. */}
      <div className="mb-6 flex flex-col gap-4 md:mb-8 md:flex-row md:items-end md:justify-between">
        <div className="min-w-0 space-y-1.5">
          <h1
            id={headingId}
            tabIndex={-1}
            className="text-xl font-semibold tracking-tight text-text outline-none md:text-2xl"
          >
            Timetable
          </h1>
          <p className="text-sm text-muted md:text-base">{caption}</p>
        </div>
        {entries.length > 0 && status && (
          <Button variant="secondary" onClick={exportIcs} className="self-start md:self-auto">
            <CalendarPlus aria-hidden="true" className="size-4" />
            Add to calendar (.ics)<span className="sr-only">, downloads a file</span>
          </Button>
        )}
      </div>
      {content}
    </>
  )
}
