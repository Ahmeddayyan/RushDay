import { Award, GraduationCap, Printer } from 'lucide-react'

import { usePublicStatus } from '@/api/endpoints/public'
import type { ResultsSemester } from '@/api/types/student'
import {
  Badge,
  Button,
  Card,
  EmptyState,
  ErrorState,
  LoadingRegion,
  PageHeader,
  Refetching,
  Skeleton,
  SkeletonText,
} from '@/components/ui'
import { DEFAULT_TIME_ZONE, formatDate, formatDecimal } from '@/lib/format'
import { useDocumentTitle } from '@/lib/useDocumentTitle'

import { HowCalculated } from './components/HowCalculated'
import { publicationSentence } from './copy'
import { ResultsTable } from './components/ResultsTable'
import { gradedCountText, gradedResults, weightedAverage } from './grades'
import { useResults } from './hooks/useResults'
import { useNow } from './time'

/**
 * Printing: only the page itself, never the navigation, top bar, footer or toasts. The shell is not
 * this stage's, so the rule lives here and applies only while this page is mounted (style-src
 * allows inline styles, 03-security.md).
 */
const PRINT_CSS = `@media print {
  header, nav, footer, aside, [data-sonner-toaster], .no-print { display: none !important; }
  main { padding: 0 !important; }
}`

/** Newest year first, autumn before spring (the API's order, kept even if a response is not sorted). */
function ordered(groups: readonly ResultsSemester[]): ResultsSemester[] {
  return [...groups].sort(
    (a, b) =>
      b.academicYear.localeCompare(a.academicYear) ||
      (a.semester === b.semester ? 0 : a.semester === 'autumn' ? -1 : 1),
  )
}

function ResultsSkeleton() {
  return (
    <LoadingRegion label="your results">
      <div className="flex flex-col gap-6">
        <div className="rounded-lg border border-border bg-surface p-4 md:p-6">
          <Skeleton className="mb-3 h-5 w-32" />
          <Skeleton className="h-10 w-24" />
        </div>
        {[0, 1].map((key) => (
          <div key={key} className="space-y-3">
            <Skeleton className="h-6 w-40" />
            <div className="rounded-lg border border-border bg-surface p-4">
              <SkeletonText lines={4} />
            </div>
          </div>
        ))}
      </div>
    </LoadingRegion>
  )
}

/**
 * `/student/results` (05-frontend.md section 10): results grouped by (academic year, semester),
 * newest first; the average so far with its indicative band; scheduled and pending semesters; the
 * institution's footnote; and a print summary headed "Unofficial results summary, not a
 * transcript". Data: `['student','results']`.
 */
export function Component() {
  useDocumentTitle('Results · RushDay')
  const query = useResults()
  const { data: status } = usePublicStatus()
  const timeZone = status?.institution.timeZone ?? DEFAULT_TIME_ZONE
  const now = useNow(60_000)

  const header = (
    <PageHeader
      title="Results"
      description="Your published marks by academic year and semester."
      actions={
        query.data && query.data.semesters.some((group) => group.state === 'published') ? (
          <Button variant="secondary" className="no-print" onClick={() => window.print()}>
            <Printer aria-hidden="true" className="size-4" />
            Print results summary
          </Button>
        ) : undefined
      }
    />
  )

  if (query.isPending) {
    return (
      <>
        {header}
        <ResultsSkeleton />
      </>
    )
  }

  if (query.isError) {
    return (
      <>
        {header}
        <Card>
          <ErrorState error={query.error} onRetry={() => void query.refetch()} />
        </Card>
      </>
    )
  }

  const groups = ordered(query.data.semesters)
  const visible = groups.flatMap((group) => (group.state === 'published' ? group.results : []))
  const average = query.data.weightedAverage ?? weightedAverage(visible)
  const graded = gradedResults(visible).length
  const scheduled = groups.find((group) => group.state === 'scheduled' && group.publishAt)
  const printedOn = formatDate(now, timeZone)

  return (
    <>
      <style>{PRINT_CSS}</style>
      <div className="mb-6 hidden border-b border-black pb-3 print:block">
        <p className="text-lg font-semibold">Unofficial results summary, not a transcript</p>
        <p className="text-sm">
          {status?.institution.name ? `${status.institution.name} · ` : ''}Printed {printedOn}
        </p>
      </div>
      {header}

      {groups.length === 0 ? (
        <Card>
          <EmptyState
            icon={GraduationCap}
            title="No results yet."
            description={
              status?.nextPublication
                ? `${publicationSentence(status.nextPublication, timeZone)}.`
                : 'Your marks appear here once the academic office publishes them.'
            }
          />
        </Card>
      ) : (
        <Refetching active={query.isFetching}>
          <div className="flex flex-col gap-8">
            <Card className="break-inside-avoid">
              <div className="flex flex-col gap-4 sm:flex-row sm:items-end sm:justify-between">
                <div>
                  <p className="text-sm font-medium text-muted">Average so far</p>
                  {average !== null ? (
                    <p className="text-4xl leading-tight font-semibold tracking-tight text-text tabular-nums">
                      {formatDecimal(average)}
                    </p>
                  ) : (
                    <p className="text-base text-muted">
                      {scheduled
                        ? 'Your first marks are on their way.'
                        : 'Nothing graded yet: absences and deferrals don’t count towards it.'}
                    </p>
                  )}
                </div>
                <div className="flex flex-wrap items-center gap-x-3 gap-y-2">
                  {query.data.classification && average !== null && (
                    <Badge variant="info" icon={Award}>
                      Indicative band: {query.data.classification}
                    </Badge>
                  )}
                  {average !== null && (
                    <span className="text-sm text-muted">{gradedCountText(graded)}</span>
                  )}
                </div>
              </div>
              <HowCalculated className="mt-4" />
            </Card>

            {groups.map((group) => (
              <ResultsTable
                key={`${group.academicYear}-${group.semester}`}
                group={group}
                timeZone={timeZone}
              />
            ))}

            {status?.institution.resultsFootnote && (
              <p className="text-sm text-muted">{status.institution.resultsFootnote}</p>
            )}
          </div>
        </Refetching>
      )}
    </>
  )
}
