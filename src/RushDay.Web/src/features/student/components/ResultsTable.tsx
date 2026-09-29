import { CalendarClock, CircleCheck, Hourglass, RefreshCw } from 'lucide-react'

import type { ResultsSemester } from '@/api/types/student'
import {
  AmendedBadge,
  Button,
  Card,
  Countdown,
  Skeleton,
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeaderCell,
  TableRow,
} from '@/components/ui'
import { formatDateTime, formatShortDate } from '@/lib/format'

import { termLabel } from '../copy'
import { outcomeLabel } from '../grades'
import { useResultsRelease } from '../hooks/useResultsRelease'

import { MarkBand } from './MarkBand'

export interface ResultsTableProps {
  group: ResultsSemester
  timeZone: string
}

function groupId(group: Pick<ResultsSemester, 'academicYear' | 'semester'>): string {
  return `results-${group.semester}-${group.academicYear.replace('/', '-')}`
}

/** A scheduled (year, semester): the instant, a countdown, and the release when it passes. */
function ScheduledGroup({ group, headingId }: { group: ResultsSemester; headingId: string }) {
  const release = useResultsRelease(group.publishAt, {
    onReleased: () => document.getElementById(headingId)?.focus(),
  })
  const label = termLabel(group.semester, group.academicYear)

  if (!group.publishAt) {
    return <p className="text-sm text-muted">Results not yet published</p>
  }

  return (
    <div className="space-y-3">
      <p className="flex items-start gap-2 text-sm font-medium text-text">
        <CalendarClock aria-hidden="true" className="mt-0.5 size-4 shrink-0 text-warning" />
        Publishes {formatDateTime(group.publishAt, release.timeZone)}
      </p>
      {release.phase === 'counting' && (
        <Countdown
          target={group.publishAt}
          timeZone={release.timeZone}
          serverOffsetMs={release.offsetMs}
          sentencePrefix={`${label} results publish at`}
          elapsedText="Results are being released"
        />
      )}
      {release.phase === 'releasing' && (
        <div className="space-y-2">
          <Skeleton className="h-5 w-2/3" />
          <p role="status" className="text-sm font-medium text-text">
            Results are being released…
          </p>
        </div>
      )}
      {release.phase === 'failed' && (
        <div className="space-y-2">
          <p className="text-sm text-text">
            Your results have been published, but the portal is very busy right now.
          </p>
          <Button variant="secondary" size="sm" onClick={release.retry}>
            <RefreshCw aria-hidden="true" className="size-4" />
            Show my results
          </Button>
        </div>
      )}
    </div>
  )
}

/**
 * One (academic year, semester) of `/student/results` (05-frontend.md section 10): a table captioned
 * "Autumn 2025/26" with code, title, credits, the mark (or Absent / Deferred), the band, the date it
 * was published and "Amended {date}" for a corrected mark. A scheduled group shows when it publishes
 * (and releases itself at that instant); a pending group says the results are not published yet.
 */
export function ResultsTable({ group, timeZone }: ResultsTableProps) {
  const label = termLabel(group.semester, group.academicYear)
  const headingId = groupId(group)

  return (
    <section aria-labelledby={headingId} className="flex flex-col gap-3 break-inside-avoid">
      <div className="flex flex-wrap items-center justify-between gap-2">
        <h2
          id={headingId}
          tabIndex={-1}
          className="text-lg font-semibold tracking-tight text-text outline-none"
        >
          {label}
        </h2>
        {group.state === 'published' && (
          <span className="inline-flex items-center gap-1.5 text-sm text-muted">
            <CircleCheck aria-hidden="true" className="size-4 text-success" />
            Published
          </span>
        )}
      </div>

      {/* Fixed column widths, so the columns of every semester's table line up down the page. */}
      {group.state === 'published' ? (
        <Table caption={label} captionHidden className="sm:table-fixed">
          <TableHead>
            <TableRow>
              <TableHeaderCell className="sm:w-28">Code</TableHeaderCell>
              <TableHeaderCell>Module</TableHeaderCell>
              <TableHeaderCell numeric className="sm:w-24">
                Credits
              </TableHeaderCell>
              <TableHeaderCell numeric className="sm:w-24">
                Mark
              </TableHeaderCell>
              <TableHeaderCell className="sm:w-32">Band</TableHeaderCell>
              <TableHeaderCell className="sm:w-72">Published</TableHeaderCell>
            </TableRow>
          </TableHead>
          <TableBody>
            {[...group.results]
              .sort((a, b) => a.moduleCode.localeCompare(b.moduleCode))
              .map((result) => (
                <TableRow key={result.moduleCode}>
                  <TableCell label="Code" className="font-mono">
                    {result.moduleCode}
                  </TableCell>
                  <TableCell label="Module">{result.moduleTitle}</TableCell>
                  <TableCell label="Credits" numeric>
                    {result.credits}
                  </TableCell>
                  <TableCell label="Mark" numeric>
                    {result.outcome === 'mark' && result.mark !== null
                      ? result.mark
                      : outcomeLabel(result.outcome === 'mark' ? 'deferred' : result.outcome)}
                  </TableCell>
                  <TableCell label="Band">
                    <MarkBand outcome={result.outcome} mark={result.mark} />
                  </TableCell>
                  <TableCell label="Published">
                    <span className="inline-flex flex-wrap items-center justify-end gap-2 sm:justify-start">
                      <time dateTime={result.publishedAt} className="whitespace-nowrap">
                        {formatShortDate(result.publishedAt, timeZone)}
                      </time>
                      {result.correctedAt && (
                        <AmendedBadge correctedAt={result.correctedAt} timeZone={timeZone} />
                      )}
                    </span>
                  </TableCell>
                </TableRow>
              ))}
          </TableBody>
        </Table>
      ) : (
        <Card>
          {group.state === 'scheduled' ? (
            <ScheduledGroup group={group} headingId={headingId} />
          ) : (
            <p className="flex items-center gap-2 text-sm text-muted">
              <Hourglass aria-hidden="true" className="size-4 shrink-0" />
              Results not yet published
            </p>
          )}
        </Card>
      )}
    </section>
  )
}
