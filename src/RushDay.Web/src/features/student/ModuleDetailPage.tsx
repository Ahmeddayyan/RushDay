import { ArrowLeft, Ban, BookOpen, CalendarDays, CircleCheck, UserRound } from 'lucide-react'
import { Link, useParams } from 'react-router'

import { isApiError } from '@/api/client'
import { usePublicStatus } from '@/api/endpoints/public'
import type { ModuleDetail, MyEnrolment } from '@/api/types/common'
import type { CompletedModule } from '@/api/types/student'
import {
  Badge,
  ButtonLink,
  Card,
  CardHeader,
  CardTitle,
  EmptyState,
  ErrorState,
  LoadingRegion,
  PageHeader,
  Skeleton,
  SkeletonText,
} from '@/components/ui'
import { DEFAULT_TIME_ZONE, formatDate, formatSemester } from '@/lib/format'
import { normaliseModuleCode } from '@/lib/moduleCode'
import { useDocumentTitle } from '@/lib/useDocumentTitle'

import { CapacityMeter } from './components/CapacityMeter'
import { EnrolButton } from './components/EnrolButton'
import { lecturerName } from './copy'
import { creditsUsed, findRow, isCurrentYearRow } from './enrolState'
import { completedResultText } from './grades'
import { useDashboard } from './hooks/useDashboard'
import { useEnrol } from './hooks/useEnrol'
import { useModule } from './hooks/useModule'
import { useMyEnrolments } from './hooks/useMyEnrolments'
import { useWithdraw } from './hooks/useWithdraw'
import { timeRange, WEEKDAYS, weekdayLabel } from './time'
import { KIND_LABEL } from './timetable'

function statusSentences({
  module,
  row,
  currentYear,
  completed,
  timeZone,
}: {
  module: ModuleDetail
  row: MyEnrolment | undefined
  currentYear: string | null
  completed: CompletedModule | null | undefined
  timeZone: string
}): string[] {
  if (!row) return ['Not enrolled']
  if (row.status === 'withdrawn') {
    const withdrew = row.withdrawnAt
      ? `You withdrew on ${formatDate(row.withdrawnAt, timeZone)}.`
      : 'You withdrew from this module.'
    return module.enrolmentState === 'open' && module.isActive
      ? [withdrew, 'You can re-enrol while places remain.']
      : [withdrew]
  }
  if (!isCurrentYearRow(row, currentYear)) {
    const result = completed ? completedResultText(completed) : 'Result not yet published'
    // "mark 68 (2:1)", "Absent", "Deferred" or "result not yet published".
    const text = /^(Mark|Result)/.test(result)
      ? result.charAt(0).toLowerCase() + result.slice(1)
      : result
    return [`Completed ${row.academicYear}: ${text}`]
  }
  return [`Enrolled since ${formatDate(row.enrolledAt, timeZone)}`]
}

function DetailSkeleton() {
  return (
    <LoadingRegion label="the module">
      <div className="mb-8 space-y-3">
        <Skeleton className="h-4 w-20" />
        <Skeleton className="h-8 w-80 max-w-full" />
        <Skeleton className="h-5 w-64 max-w-full" />
      </div>
      <div className="grid gap-4 md:gap-6 lg:grid-cols-[minmax(0,2fr)_minmax(0,1fr)]">
        <div className="rounded-lg border border-border bg-surface p-4 md:p-6">
          <SkeletonText lines={5} />
        </div>
        <div className="rounded-lg border border-border bg-surface p-4 md:p-6">
          <Skeleton className="mb-4 h-3 w-full" />
          <Skeleton className="h-10 w-36" />
        </div>
      </div>
    </LoadingRegion>
  )
}

/**
 * `/student/modules/:code` (05-frontend.md section 10): the module with live places (refetched every
 * 10 s while its semester's enrolment is open and the tab is visible), description, timetable, the
 * student's status and the same enrol control as the catalogue. Data: `['modules', code]`,
 * `['student','enrolments']`, `['student','dashboard']`.
 */
export function Component() {
  const params = useParams()
  const code = normaliseModuleCode(params.code ?? '')
  useDocumentTitle(`${code} · RushDay`)

  const query = useModule(code)
  const { query: enrolments, isFresh } = useMyEnrolments()
  const { data: dashboard } = useDashboard()
  const { data: status } = usePublicStatus()
  const control = useEnrol(code)
  const withdrawal = useWithdraw(code)
  const timeZone = status?.institution.timeZone ?? DEFAULT_TIME_ZONE
  const currentYear = status?.academicYear ?? dashboard?.academicYear ?? null

  const back = (
    <Link
      to="/student/modules"
      className="inline-flex items-center gap-1 rounded-sm text-sm font-medium text-primary hover:underline"
    >
      <ArrowLeft aria-hidden="true" className="size-4" />
      All modules
    </Link>
  )

  const notFound = (
    <>
      <PageHeader title="Module not found" eyebrow={back} />
      <Card>
        <EmptyState
          icon={BookOpen}
          title="That page or record doesn't exist."
          description={`There is no module ${code || 'with that code'} in the catalogue.`}
          action={
            <ButtonLink to="/student/modules" variant="secondary">
              Browse modules
            </ButtonLink>
          }
        />
      </Card>
    </>
  )

  if (!/^[A-Z]{2}\d{4}$/.test(code)) return notFound
  if (query.isPending) return <DetailSkeleton />
  if (query.isError) {
    if (isApiError(query.error) && query.error.status === 404) return notFound
    return (
      <>
        <PageHeader title={code} eyebrow={back} />
        <Card>
          <ErrorState error={query.error} context={{ code }} onRetry={() => void query.refetch()} />
        </Card>
      </>
    )
  }

  const module = query.data
  const row = findRow(enrolments.data, module.code)
  const completed = dashboard
    ? (dashboard.completed.find((item) => item.code === module.code) ?? null)
    : undefined
  const slots = [...module.timetable].sort(
    (a, b) =>
      WEEKDAYS.indexOf(a.day) - WEEKDAYS.indexOf(b.day) || a.startTime.localeCompare(b.startTime),
  )
  const lecturers = [...module.lecturers].sort((a, b) =>
    a.role === b.role ? a.fullName.localeCompare(b.fullName) : a.role === 'leader' ? -1 : 1,
  )

  return (
    <>
      <PageHeader
        title={module.title}
        eyebrow={
          <span className="flex flex-wrap items-center gap-x-4 gap-y-1">
            {back}
            <span className="font-mono">{module.code}</span>
          </span>
        }
      >
        <div className="flex flex-wrap gap-2 pt-1">
          <Badge>{module.credits} credits</Badge>
          <Badge>{formatSemester(module.semester)}</Badge>
          <Badge>Level {module.level}</Badge>
          {lecturers.map((lecturer) => (
            <Badge key={lecturer.staffNumber} icon={UserRound}>
              {lecturerName(lecturer)}
              {lecturer.role === 'leader' ? ' (leader)' : ''}
              {lecturer.left ? ' (left)' : ''}
            </Badge>
          ))}
        </div>
      </PageHeader>

      {!module.isActive && (
        <p className="mb-6 flex items-start gap-2 rounded-lg border border-warning/25 bg-warning-soft px-4 py-3 text-sm font-medium text-warning">
          <Ban aria-hidden="true" className="mt-0.5 size-4 shrink-0" />
          This module is no longer running
        </p>
      )}

      <div className="grid items-start gap-4 md:gap-6 lg:grid-cols-[minmax(0,2fr)_minmax(0,1fr)]">
        <div className="flex min-w-0 flex-col gap-4 md:gap-6">
          <Card>
            <CardHeader>
              <CardTitle>About this module</CardTitle>
            </CardHeader>
            <p className="text-sm leading-relaxed whitespace-pre-line text-text">
              {module.description ?? 'No description has been published for this module yet.'}
            </p>
          </Card>
          <Card>
            <CardHeader>
              <CardTitle>Timetable</CardTitle>
              <p className="text-sm text-muted">
                Weekly, {formatSemester(module.semester)} semester
              </p>
            </CardHeader>
            {slots.length === 0 ? (
              <EmptyState
                compact
                icon={CalendarDays}
                headingLevel="p"
                title="No classes are scheduled for this module yet."
              />
            ) : (
              <ul className="divide-y divide-border">
                {slots.map((slot) => (
                  <li
                    key={`${slot.day}-${slot.startTime}-${slot.room}`}
                    className="flex flex-wrap gap-x-4 gap-y-1 py-2.5 text-sm first:pt-0 last:pb-0"
                  >
                    <span className="w-24 font-medium text-text">{weekdayLabel(slot.day)}</span>
                    <span className="w-24 text-text tabular-nums">
                      {timeRange(slot.startTime, slot.endTime)}
                    </span>
                    <span className="text-muted">
                      {KIND_LABEL[slot.kind]} · {slot.room}
                    </span>
                  </li>
                ))}
              </ul>
            )}
          </Card>
        </div>

        <Card className="flex flex-col gap-5">
          <div>
            <h2 className="mb-3 text-lg font-semibold tracking-tight text-text">Places</h2>
            <CapacityMeter module={module} filledAt={control.filledAt} size="lg" />
          </div>
          <div className="border-t border-border pt-5">
            <h2 className="mb-2 text-lg font-semibold tracking-tight text-text">Your status</h2>
            {enrolments.isPending ? (
              <SkeletonText lines={2} />
            ) : enrolments.isError ? (
              <ErrorState
                compact
                headingLevel="p"
                error={enrolments.error}
                onRetry={() => void enrolments.refetch()}
              />
            ) : (
              <>
                <div className="mb-4 space-y-1 text-sm text-text">
                  {statusSentences({ module, row, currentYear, completed, timeZone }).map(
                    (sentence, index) => (
                      <p key={sentence} className="flex items-start gap-2">
                        {index === 0 && row?.status === 'active' && (
                          <CircleCheck
                            aria-hidden="true"
                            className="mt-0.5 size-4 shrink-0 text-success"
                          />
                        )}
                        {sentence}
                      </p>
                    ),
                  )}
                </div>
                <EnrolButton
                  module={module}
                  row={row}
                  currentYear={currentYear}
                  creditsUsed={creditsUsed(enrolments.data, module.semester, currentYear)}
                  enrolmentsFresh={isFresh}
                  control={control}
                  onWithdraw={() => withdrawal.mutate()}
                  completed={completed}
                  timeZone={timeZone}
                  size="lg"
                />
              </>
            )}
          </div>
        </Card>
      </div>
    </>
  )
}
