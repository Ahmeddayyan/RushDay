import { useServerClock } from '@/api/endpoints/public'
import {
  Card,
  ErrorState,
  LoadingRegion,
  PageHeader,
  Refetching,
  Skeleton,
  SkeletonText,
} from '@/components/ui'
import { useDocumentTitle } from '@/lib/useDocumentTitle'

import { AnnouncementsCard } from './components/AnnouncementsCard'
import { CompletedModulesCard } from './components/CompletedModulesCard'
import { CurrentModulesCard } from './components/CurrentModulesCard'
import { EnrolmentWindowCard } from './components/EnrolmentWindowCard'
import { NextClassesCard } from './components/NextClassesCard'
import { ResultsSummaryCard } from './components/ResultsSummaryCard'
import { useDashboard } from './hooks/useDashboard'
import { greeting, useZonedNow } from './time'

function firstName(fullName: string): string {
  return fullName.trim().split(/\s+/)[0] ?? fullName
}

function yearOfStudyText(year: number): string {
  return `Year ${year}`
}

function DashboardSkeleton() {
  return (
    <LoadingRegion label="your dashboard">
      <div className="mb-8 space-y-2">
        <Skeleton className="h-8 w-64" />
        <Skeleton className="h-4 w-80 max-w-full" />
      </div>
      <div className="grid gap-4 md:gap-6 lg:grid-cols-2">
        {[0, 1, 2, 3].map((key) => (
          <div key={key} className="rounded-lg border border-border bg-surface p-4 md:p-6">
            <Skeleton className="mb-4 h-6 w-40" />
            <Skeleton className="mb-3 h-10 w-24" />
            <SkeletonText lines={3} />
          </div>
        ))}
      </div>
    </LoadingRegion>
  )
}

/**
 * `/student` (05-frontend.md section 10): greeting, results with the release countdown, enrolment
 * windows and credits, next classes, announcements, this year's and completed modules. One request
 * (`['student','dashboard']`) plus `['public','status']` for the server clock.
 */
export function Component() {
  useDocumentTitle('Home · RushDay')
  const query = useDashboard()
  const { offsetMs, timeZone } = useServerClock()
  const now = useZonedNow(timeZone, offsetMs)

  if (query.isPending) {
    return (
      <>
        <PageHeader title="Your dashboard" className="sr-only" />
        <DashboardSkeleton />
      </>
    )
  }

  if (query.isError) {
    return (
      <>
        <PageHeader title="Your dashboard" />
        <Card>
          <ErrorState error={query.error} onRetry={() => void query.refetch()} />
        </Card>
      </>
    )
  }

  const dashboard = query.data

  return (
    <>
      <PageHeader
        title={`${greeting(now.hour)}, ${firstName(dashboard.fullName)}`}
        description={
          <span className="flex flex-wrap gap-x-2 gap-y-1">
            <span className="font-mono">{dashboard.studentNumber}</span>
            <span aria-hidden="true">·</span>
            <span>{dashboard.programme}</span>
            <span aria-hidden="true">·</span>
            <span>{yearOfStudyText(dashboard.yearOfStudy)}</span>
            <span aria-hidden="true">·</span>
            <span>{dashboard.academicYear}</span>
          </span>
        }
      />
      <Refetching active={query.isFetching}>
        <div className="grid items-start gap-4 md:gap-6 lg:grid-cols-2">
          <div className="flex min-w-0 flex-col gap-4 md:gap-6">
            <ResultsSummaryCard dashboard={dashboard} />
            <NextClassesCard
              timetable={dashboard.timetable}
              currentSemester={dashboard.currentSemester}
              now={now}
            />
            <CurrentModulesCard modules={dashboard.modules} academicYear={dashboard.academicYear} />
          </div>
          <div className="flex min-w-0 flex-col gap-4 md:gap-6">
            <EnrolmentWindowCard
              windows={dashboard.enrolmentWindows}
              credits={dashboard.credits}
              academicYear={dashboard.academicYear}
              timeZone={timeZone}
            />
            <AnnouncementsCard announcements={dashboard.announcements} timeZone={timeZone} />
            <CompletedModulesCard completed={dashboard.completed} />
          </div>
        </div>
      </Refetching>
    </>
  )
}
