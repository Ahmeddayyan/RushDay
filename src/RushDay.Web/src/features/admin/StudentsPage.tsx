import { useState } from 'react'
import { UserPlus, UsersRound } from 'lucide-react'

import {
  Button,
  ButtonLink,
  Card,
  EmptyState,
  ErrorState,
  PageHeader,
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
import { useDebouncedValue } from '@/lib/useDebouncedValue'
import { useDocumentTitle } from '@/lib/useDocumentTitle'

import { CreateStudentDialog } from './components/CreateStudentDialog'
import { LeftBadge, StudentAccountChip } from './components/StatusChips'
import { TableSkeleton } from './components/TableSkeleton'
import { STUDENTS_PAGE_SIZE, useStudents } from './hooks/useStudents'
import { useInstitutionClock } from './lib/useInstitutionClock'
import { useOneShotFlag, useUrlFilters } from './lib/urlState'

const FILTERS = ['q'] as const

/**
 * `/admin/students` (05-frontend.md section 10): search by number prefix or any part of the name,
 * 25 per page, and "Create student" with optional account provisioning.
 */
export function Component() {
  useDocumentTitle('Students · RushDay')
  const { timeZone } = useInstitutionClock()
  const { values, page, update } = useUrlFilters(FILTERS)
  // The overview's "Create student" quick action arrives as ?create=1.
  const [createOpen, setCreateOpen] = useState(useOneShotFlag('create'))
  const q = useDebouncedValue(values.q, 250)
  const query = useStudents({ q, page })

  let content
  if (query.isPending) {
    content = <TableSkeleton label="students" />
  } else if (query.isError) {
    content = (
      <Card>
        <ErrorState error={query.error} onRetry={() => void query.refetch()} />
      </Card>
    )
  } else if (query.data.items.length === 0) {
    content = (
      <Card>
        <EmptyState
          icon={UsersRound}
          title={q ? `No students match "${q}".` : 'No students yet.'}
          description={
            q
              ? 'Search by the start of a student number or any part of a name.'
              : 'Create the first student record.'
          }
          action={<Button onClick={() => setCreateOpen(true)}>Create student</Button>}
        />
      </Card>
    )
  } else {
    content = (
      <div className="flex flex-col gap-4">
        <Refetching active={query.isPlaceholderData}>
          <Table caption="Students" captionHidden>
            <TableHead>
              <TableRow>
                <TableHeaderCell>Student number</TableHeaderCell>
                <TableHeaderCell>Name</TableHeaderCell>
                <TableHeaderCell>Programme</TableHeaderCell>
                <TableHeaderCell numeric>Year</TableHeaderCell>
                <TableHeaderCell>Account</TableHeaderCell>
                <TableHeaderCell>
                  <span className="sr-only">Actions</span>
                </TableHeaderCell>
              </TableRow>
            </TableHead>
            <TableBody>
              {query.data.items.map((student) => (
                <TableRow key={student.studentNumber}>
                  <TableCell label="Student number" className="font-mono">
                    {student.studentNumber}
                  </TableCell>
                  <TableCell label="Name">
                    <span className="flex flex-wrap items-center gap-2">
                      {student.fullName}
                      {student.leftAt && <LeftBadge leftAt={student.leftAt} timeZone={timeZone} />}
                    </span>
                  </TableCell>
                  <TableCell label="Programme">{student.programme}</TableCell>
                  <TableCell label="Year" numeric>
                    {student.yearOfStudy}
                  </TableCell>
                  <TableCell label="Account">
                    <StudentAccountChip state={student.accountState} />
                  </TableCell>
                  <TableCell className="text-right">
                    <ButtonLink
                      to={`/admin/students/${student.studentNumber}`}
                      variant="secondary"
                      size="sm"
                      aria-label={`View ${student.studentNumber} ${student.fullName}`}
                    >
                      View
                    </ButtonLink>
                  </TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        </Refetching>
        <Pagination
          page={query.data.page}
          pageSize={query.data.pageSize || STUDENTS_PAGE_SIZE}
          total={query.data.total}
          onPageChange={(next) => update({ page: next })}
          itemLabel="students"
          busy={query.isPlaceholderData}
        />
      </div>
    )
  }

  return (
    <>
      <PageHeader
        title="Students"
        description="Find a student to see their record as they see it, with statuses and history."
        actions={
          <Button onClick={() => setCreateOpen(true)}>
            <UserPlus aria-hidden="true" className="size-4" />
            Create student
          </Button>
        }
      />
      <SearchInput
        label="Search students"
        showLabel
        value={values.q}
        onChange={(next) => update({ q: next })}
        placeholder="Student number or name"
        className="mb-4 w-full sm:w-80"
      />
      {content}
      <CreateStudentDialog open={createOpen} onOpenChange={setCreateOpen} />
    </>
  )
}
