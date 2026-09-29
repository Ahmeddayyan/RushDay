import { useState } from 'react'
import { ClipboardList, PenLine } from 'lucide-react'

import type { AdminMarksRow, AdminMarksSheet } from '@/api/types/admin'
import {
  AmendedBadge,
  Button,
  Card,
  EmptyState,
  ErrorState,
  MarksStatusChip,
  Pagination,
  Refetching,
  SearchInput,
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeaderCell,
  TableRow,
} from '@/components/ui'
import { formatDateTime, formatNumber } from '@/lib/format'
import { useDebouncedValue } from '@/lib/useDebouncedValue'

import { MARKS_PAGE_SIZE, useModuleMarks } from '../hooks/useModules'
import { describeMark } from '../lib/marks'
import { useUrlFilters } from '../lib/urlState'

import { CorrectMarkDialog, type CorrectionTarget } from './CorrectMarkDialog'
import { EnrolmentStatusChip, GradeStatusChip } from './StatusChips'
import { TableSkeleton } from './TableSkeleton'

const FILTERS = ['q'] as const

function correctable(row: AdminMarksRow): boolean {
  return row.gradeStatus === 'submitted' || row.gradeStatus === 'published'
}

function MarksSummary({ sheet, timeZone }: { sheet: AdminMarksSheet; timeZone: string }) {
  return (
    <div className="flex flex-wrap items-center gap-x-6 gap-y-2 text-sm text-muted">
      <MarksStatusChip status={sheet.status} publishedAt={sheet.publishedAt} timeZone={timeZone} />
      <span className="tabular-nums">
        {formatNumber(sheet.summary.entered)} entered · {formatNumber(sheet.summary.missing)}{' '}
        missing · {formatNumber(sheet.summary.total)} students
      </span>
      {sheet.leader && <span>Leader: {sheet.leader}</span>}
      {sheet.submittedAt && <span>Submitted {formatDateTime(sheet.submittedAt, timeZone)}</span>}
    </div>
  )
}

/**
 * `ModuleMarksTable` (05-frontend.md section 10, `/admin/modules/:code/marks`): the marks sheet,
 * read-only (no inputs, no Save or Submit), with "Correct" on submitted, scheduled and published
 * rows opening `CorrectMarkDialog`.
 */
export function ModuleMarksTable({ code, timeZone }: { code: string; timeZone: string }) {
  const { values, page, update } = useUrlFilters(FILTERS)
  const q = useDebouncedValue(values.q, 250)
  const query = useModuleMarks(code, { q, page })
  const [target, setTarget] = useState<CorrectionTarget | null>(null)

  let content
  if (query.isPending) {
    content = <TableSkeleton label="marks" />
  } else if (query.isError) {
    content = (
      <Card>
        <ErrorState error={query.error} context={{ code }} onRetry={() => void query.refetch()} />
      </Card>
    )
  } else {
    const sheet = query.data
    // Published grades of a publication whose instant is still ahead read "Scheduled".
    const live = sheet.status !== 'scheduled'
    content = (
      <div className="flex flex-col gap-4">
        <MarksSummary sheet={sheet} timeZone={timeZone} />
        {sheet.rows.length === 0 ? (
          <Card>
            <EmptyState
              icon={ClipboardList}
              title={
                values.q
                  ? `No students on ${code} match "${values.q}".`
                  : `No students are enrolled on ${code} this year.`
              }
            />
          </Card>
        ) : (
          <>
            <Refetching active={query.isPlaceholderData}>
              <Table caption={`Marks for ${code}, read-only`} captionHidden>
                <TableHead>
                  <TableRow>
                    <TableHeaderCell>Student number</TableHeaderCell>
                    <TableHeaderCell>Name</TableHeaderCell>
                    <TableHeaderCell numeric>Mark</TableHeaderCell>
                    <TableHeaderCell>Status</TableHeaderCell>
                    <TableHeaderCell>Updated</TableHeaderCell>
                    <TableHeaderCell>Entered by</TableHeaderCell>
                    <TableHeaderCell>
                      <span className="sr-only">Actions</span>
                    </TableHeaderCell>
                  </TableRow>
                </TableHead>
                <TableBody>
                  {sheet.rows.map((row) => (
                    <TableRow
                      key={row.studentNumber}
                      className={row.enrolmentStatus === 'withdrawn' ? 'text-muted' : undefined}
                    >
                      <TableCell label="Student number" className="font-mono">
                        {row.studentNumber}
                      </TableCell>
                      <TableCell label="Name">
                        <span className="flex flex-wrap items-center gap-2">
                          {row.fullName}
                          {row.enrolmentStatus === 'withdrawn' && (
                            <EnrolmentStatusChip status="withdrawn" />
                          )}
                        </span>
                      </TableCell>
                      <TableCell label="Mark" numeric>
                        {row.outcome === null ? (
                          <span className="text-muted">Missing</span>
                        ) : (
                          describeMark(row)
                        )}
                      </TableCell>
                      <TableCell label="Status">
                        <span className="flex flex-wrap gap-1.5">
                          {row.gradeStatus && (
                            <GradeStatusChip status={row.gradeStatus} live={live} />
                          )}
                          {row.correctedAt && (
                            <AmendedBadge correctedAt={row.correctedAt} timeZone={timeZone} />
                          )}
                        </span>
                      </TableCell>
                      <TableCell label="Updated">
                        {row.updatedAt
                          ? formatDateTime(row.updatedAt, timeZone, { zone: false })
                          : ''}
                      </TableCell>
                      <TableCell label="Entered by">{row.enteredBy ?? ''}</TableCell>
                      <TableCell className="text-right">
                        {correctable(row) && (
                          <Button
                            variant="secondary"
                            size="sm"
                            aria-label={`Correct the mark of ${row.studentNumber} ${row.fullName}`}
                            onClick={() =>
                              setTarget({
                                code,
                                studentNumber: row.studentNumber,
                                studentName: row.fullName,
                                current: { outcome: row.outcome, mark: row.mark },
                                live: row.gradeStatus === 'published' && live,
                              })
                            }
                          >
                            <PenLine aria-hidden="true" className="size-4" />
                            Correct
                          </Button>
                        )}
                      </TableCell>
                    </TableRow>
                  ))}
                </TableBody>
              </Table>
            </Refetching>
            <Pagination
              page={sheet.page}
              pageSize={sheet.pageSize || MARKS_PAGE_SIZE}
              total={sheet.total}
              onPageChange={(next) => update({ page: next })}
              itemLabel="students"
              busy={query.isPlaceholderData}
            />
          </>
        )}
      </div>
    )
  }

  return (
    <div className="flex flex-col gap-4">
      <p className="text-sm text-muted">
        Read-only. Lecturers enter marks; the registry corrects single marks after submission.
      </p>
      <SearchInput
        label={`Search the marks of ${code}`}
        value={values.q}
        onChange={(next) => update({ q: next })}
        placeholder="Student number or name"
        className="w-full sm:w-80"
      />
      {content}
      <CorrectMarkDialog target={target} onOpenChange={(open) => !open && setTarget(null)} />
    </div>
  )
}
