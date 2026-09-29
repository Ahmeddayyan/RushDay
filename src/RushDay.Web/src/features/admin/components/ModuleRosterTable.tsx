import { UsersRound } from 'lucide-react'
import { Link } from 'react-router'

import {
  Card,
  EmptyState,
  ErrorState,
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
import { formatShortDate } from '@/lib/format'
import { useDebouncedValue } from '@/lib/useDebouncedValue'

import { ROSTER_PAGE_SIZE, useModuleRoster } from '../hooks/useModules'
import { useUrlFilters } from '../lib/urlState'

import { EnrolmentStatusChip } from './StatusChips'
import { TableSkeleton } from './TableSkeleton'

const FILTERS = ['q'] as const

/**
 * `ModuleRosterTable` (05-frontend.md section 10, `/admin/modules/:code/roster`): the lecturer
 * roster's columns for any module, 50 per page, searched by number prefix or any part of the name;
 * search and page live in the URL.
 */
export function ModuleRosterTable({ code, timeZone }: { code: string; timeZone: string }) {
  const { values, page, update } = useUrlFilters(FILTERS)
  const q = useDebouncedValue(values.q, 250)
  const query = useModuleRoster(code, { q, page })

  let content
  if (query.isPending) {
    content = <TableSkeleton label="roster" />
  } else if (query.isError) {
    content = (
      <Card>
        <ErrorState error={query.error} context={{ code }} onRetry={() => void query.refetch()} />
      </Card>
    )
  } else if (query.data.items.length === 0) {
    content = (
      <Card>
        <EmptyState
          icon={UsersRound}
          title={
            values.q
              ? `No students on ${code} match "${values.q}".`
              : `No students are enrolled on ${code} this year.`
          }
          description="Students appear here when they enrol, or when you enrol them from their record."
        />
      </Card>
    )
  } else {
    content = (
      <div className="flex flex-col gap-4">
        <Refetching active={query.isPlaceholderData}>
          <Table caption={`Students on ${code}`} captionHidden>
            <TableHead>
              <TableRow>
                <TableHeaderCell>Student number</TableHeaderCell>
                <TableHeaderCell>Name</TableHeaderCell>
                <TableHeaderCell>Programme</TableHeaderCell>
                <TableHeaderCell numeric>Year</TableHeaderCell>
                <TableHeaderCell>Enrolled on</TableHeaderCell>
                <TableHeaderCell>Status</TableHeaderCell>
              </TableRow>
            </TableHead>
            <TableBody>
              {query.data.items.map((row) => (
                <TableRow key={row.studentNumber}>
                  <TableCell label="Student number" className="font-mono">
                    <Link
                      to={`/admin/students/${row.studentNumber}`}
                      className="text-primary underline-offset-2 hover:underline"
                    >
                      {row.studentNumber}
                    </Link>
                  </TableCell>
                  <TableCell label="Name">{row.fullName}</TableCell>
                  <TableCell label="Programme">{row.programme}</TableCell>
                  <TableCell label="Year" numeric>
                    {row.yearOfStudy}
                  </TableCell>
                  <TableCell label="Enrolled on">
                    {formatShortDate(row.enrolledAt, timeZone)}
                  </TableCell>
                  <TableCell label="Status">
                    <EnrolmentStatusChip status={row.status} />
                  </TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        </Refetching>
        <Pagination
          page={query.data.page}
          pageSize={query.data.pageSize || ROSTER_PAGE_SIZE}
          total={query.data.total}
          onPageChange={(next) => update({ page: next })}
          itemLabel="students"
          busy={query.isPlaceholderData}
        />
      </div>
    )
  }

  return (
    <div className="flex flex-col gap-4">
      <SearchInput
        label={`Search the students on ${code}`}
        value={values.q}
        onChange={(next) => update({ q: next })}
        placeholder="Student number or name"
        className="w-full sm:w-80"
      />
      {content}
    </div>
  )
}
