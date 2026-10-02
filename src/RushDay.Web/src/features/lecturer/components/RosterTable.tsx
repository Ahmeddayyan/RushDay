import { UserCheck, UserX } from 'lucide-react'

import type { RosterEntry } from '@/api/types/lecturer'
import { Badge, Pagination, Refetching, Table, TableBody, TableCell, TableHead, TableHeaderCell, TableRow } from '@/components/ui'
import { formatShortDate } from '@/lib/format'

export interface RosterTableProps {
  code: string
  rows: RosterEntry[]
  page: number
  pageSize: number
  total: number
  onPageChange: (page: number) => void
  fetching?: boolean
  timeZone?: string
}

/** The roster of one module (05-frontend.md section 10): number, name, programme, year, enrolled on, status. */
export function RosterTable({
  code,
  rows,
  page,
  pageSize,
  total,
  onPageChange,
  fetching = false,
  timeZone,
}: RosterTableProps) {
  return (
    <div className="flex flex-col gap-4">
      <Refetching active={fetching}>
        <Table caption={`Roster for ${code}`} captionHidden>
          <TableHead>
            <TableRow>
              <TableHeaderCell>Number</TableHeaderCell>
              <TableHeaderCell>Name</TableHeaderCell>
              <TableHeaderCell>Programme</TableHeaderCell>
              <TableHeaderCell numeric>Year</TableHeaderCell>
              <TableHeaderCell>Enrolled on</TableHeaderCell>
              <TableHeaderCell>Status</TableHeaderCell>
            </TableRow>
          </TableHead>
          <TableBody>
            {rows.map((row) => (
              <TableRow key={row.studentNumber}>
                <TableCell label="Number" className="font-mono">
                  {row.studentNumber}
                </TableCell>
                <TableCell label="Name">{row.fullName}</TableCell>
                <TableCell label="Programme">{row.programme}</TableCell>
                <TableCell label="Year" numeric>
                  {row.yearOfStudy}
                </TableCell>
                <TableCell label="Enrolled on">{formatShortDate(row.enrolledAt, timeZone)}</TableCell>
                <TableCell label="Status">
                  {row.status === 'active' ? (
                    <Badge variant="success" icon={UserCheck}>
                      Active
                    </Badge>
                  ) : (
                    <Badge variant="neutral" icon={UserX}>
                      Withdrawn
                    </Badge>
                  )}
                </TableCell>
              </TableRow>
            ))}
          </TableBody>
        </Table>
      </Refetching>
      <Pagination
        page={page}
        pageSize={pageSize}
        total={total}
        onPageChange={onPageChange}
        itemLabel="students"
        busy={fetching}
      />
    </div>
  )
}
