import { useRef } from 'react'
import { ArrowRight, Award, CalendarClock, CircleCheck, RefreshCw } from 'lucide-react'
import { Link } from 'react-router'

import type { DashboardResponse } from '@/api/types/student'
import {
  Badge,
  Button,
  Card,
  CardHeader,
  CardTitle,
  Countdown,
  Skeleton,
  SkeletonText,
} from '@/components/ui'
import { formatDecimal } from '@/lib/format'

import { publicationSentence, termLabel } from '../copy'
import { gradedCountText, gradedResults } from '../grades'
import { useResultsRelease } from '../hooks/useResultsRelease'

import { HowCalculated } from './HowCalculated'

export interface ResultsSummaryCardProps {
  dashboard: Pick<
    DashboardResponse,
    'weightedAverage' | 'classification' | 'results' | 'nextPublication' | 'latestPublication'
  >
}

/**
 * The dashboard's results card (05-frontend.md section 10, `/student`): "Average so far" with the
 * indicative band and how many modules are graded, how it is calculated, and the next publication
 * with a countdown that releases the results at the instant (`useResultsRelease`).
 */
export function ResultsSummaryCard({ dashboard }: ResultsSummaryCardProps) {
  const headingRef = useRef<HTMLHeadingElement>(null)
  const next = dashboard.nextPublication
  const latest = dashboard.latestPublication?.state === 'live' ? dashboard.latestPublication : null
  const release = useResultsRelease(next?.publishAt, {
    onReleased: () => headingRef.current?.focus(),
  })

  const graded = gradedResults(dashboard.results).length
  const average = dashboard.weightedAverage

  return (
    <Card className="relative">
      <CardHeader
        actions={
          <Link
            to="/student/results"
            className="inline-flex items-center gap-1 rounded-sm text-sm font-medium text-primary hover:underline"
          >
            All results
            <ArrowRight aria-hidden="true" className="size-4" />
          </Link>
        }
      >
        <CardTitle ref={headingRef} tabIndex={-1} className="outline-none">
          Your results
        </CardTitle>
      </CardHeader>

      <div className="flex flex-col gap-5">
        {average !== null ? (
          <div className="space-y-3">
            <div>
              <p className="text-sm font-medium text-muted">Average so far</p>
              <p className="text-4xl leading-tight font-semibold tracking-tight text-text tabular-nums">
                {formatDecimal(average)}
              </p>
            </div>
            <div className="flex flex-wrap items-center gap-x-3 gap-y-2">
              {dashboard.classification && (
                <Badge variant="info" icon={Award}>
                  Indicative band: {dashboard.classification}
                </Badge>
              )}
              <span className="text-sm text-muted">{gradedCountText(graded)}</span>
            </div>
            <HowCalculated />
          </div>
        ) : dashboard.results.length > 0 ? (
          <p className="text-sm text-muted">
            Nothing to average yet: absences and deferrals don&apos;t count towards it.
          </p>
        ) : (
          !next &&
          release.phase !== 'released' && (
            <p className="text-sm text-muted">
              No results yet. Your lecturers haven&apos;t published anything.
            </p>
          )
        )}

        <div role="status" className="empty:hidden">
          {release.phase === 'released' && (
            <p className="flex items-center gap-2 text-sm font-medium text-success">
              <CircleCheck aria-hidden="true" className="size-4.5 shrink-0" />
              Your results are in
            </p>
          )}
        </div>

        {next && release.phase === 'counting' && (
          <div className="space-y-3 rounded-lg border border-border bg-surface-2 p-4">
            <p className="flex items-start gap-2 text-sm font-medium text-text">
              <CalendarClock aria-hidden="true" className="mt-0.5 size-4 shrink-0 text-warning" />
              {publicationSentence(next, release.timeZone)}
            </p>
            <Countdown
              target={next.publishAt}
              timeZone={release.timeZone}
              serverOffsetMs={release.offsetMs}
              sentencePrefix={`${termLabel(next.semester, next.academicYear)} results publish at`}
              elapsedText="Results are being released"
            />
          </div>
        )}

        {release.phase === 'failed' && (
          <div className="space-y-3 rounded-lg border border-border bg-surface-2 p-4">
            <p className="text-sm text-text">
              Your results have been published, but the portal is very busy right now.
            </p>
            <Button variant="secondary" size="sm" onClick={release.retry}>
              <RefreshCw aria-hidden="true" className="size-4" />
              Show my results
            </Button>
          </div>
        )}

        {!next && latest && release.phase !== 'failed' && (
          <p className="flex items-start gap-2 text-sm text-muted">
            <CircleCheck aria-hidden="true" className="mt-0.5 size-4 shrink-0 text-success" />
            {publicationSentence(latest, release.timeZone)}
          </p>
        )}
      </div>

      {release.phase === 'releasing' && (
        <div className="absolute inset-0 z-10 flex flex-col justify-center gap-4 rounded-lg bg-surface/95 p-4 md:p-6">
          <Skeleton className="h-9 w-28" />
          <SkeletonText lines={2} />
          <p role="status" className="text-sm font-medium text-text">
            Results are being released…
          </p>
        </div>
      )}
    </Card>
  )
}
