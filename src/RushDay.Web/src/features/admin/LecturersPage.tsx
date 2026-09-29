import { useState } from 'react'
import { CircleCheck, CircleSlash, IdCard, LogOut, Pencil, UserPlus } from 'lucide-react'
import { Link } from 'react-router'

import type { AdminLecturer } from '@/api/types/admin'
import {
  Badge,
  Button,
  Card,
  EmptyState,
  ErrorState,
  PageHeader,
  Refetching,
  SearchInput,
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeaderCell,
  TableRow,
} from '@/components/ui'
import { formatNumber } from '@/lib/format'
import { useDebouncedValue } from '@/lib/useDebouncedValue'
import { useDocumentTitle } from '@/lib/useDocumentTitle'

import { CreateLecturerDialog } from './components/CreateLecturerDialog'
import { EditLecturerDialog } from './components/EditLecturerDialog'
import { MarkLecturerLeftDialog } from './components/MarkLecturerLeftDialog'
import { LeftBadge } from './components/StatusChips'
import { TableSkeleton } from './components/TableSkeleton'
import { useLecturers } from './hooks/useLecturers'
import { useInstitutionClock } from './lib/useInstitutionClock'
import { useUrlFilters } from './lib/urlState'

const FILTERS = ['q'] as const

function ModuleLinks({ codes }: { codes: string[] }) {
  if (codes.length === 0) return <span className="text-muted">None</span>
  return (
    <span className="flex flex-wrap gap-x-2 gap-y-1">
      {codes.map((code) => (
        <Link
          key={code}
          to={`/admin/modules/${code}`}
          className="font-mono text-primary underline-offset-2 hover:underline"
        >
          {code}
        </Link>
      ))}
    </span>
  )
}

/**
 * `/admin/lecturers` (05-frontend.md section 10): staff number, name, title, department, modules,
 * account state and "Left"; Edit, Mark as left and Create lecturer.
 */
export function Component() {
  useDocumentTitle('Lecturers · RushDay')
  const { timeZone } = useInstitutionClock()
  const { values, update } = useUrlFilters(FILTERS)
  const q = useDebouncedValue(values.q, 250)
  const query = useLecturers(q)
  const [createOpen, setCreateOpen] = useState(false)
  const [editing, setEditing] = useState<AdminLecturer | null>(null)
  const [leaving, setLeaving] = useState<AdminLecturer | null>(null)

  let content
  if (query.isPending) {
    content = <TableSkeleton label="lecturers" />
  } else if (query.isError) {
    content = (
      <Card>
        <ErrorState error={query.error} onRetry={() => void query.refetch()} />
      </Card>
    )
  } else if (query.data.length === 0) {
    content = (
      <Card>
        <EmptyState
          icon={IdCard}
          title={q ? `No lecturers match "${q}".` : 'No lecturers yet.'}
          description="Create a lecturer, then assign them to modules and provision an account."
          action={<Button onClick={() => setCreateOpen(true)}>Create lecturer</Button>}
        />
      </Card>
    )
  } else {
    content = (
      <Refetching active={query.isPlaceholderData}>
        <p className="mb-3 text-sm text-muted" aria-live="polite">
          {formatNumber(query.data.length)} lecturers
        </p>
        <Table caption="Lecturers" captionHidden>
          <TableHead>
            <TableRow>
              <TableHeaderCell>Staff number</TableHeaderCell>
              <TableHeaderCell>Name</TableHeaderCell>
              <TableHeaderCell>Title</TableHeaderCell>
              <TableHeaderCell>Department</TableHeaderCell>
              <TableHeaderCell>Modules</TableHeaderCell>
              <TableHeaderCell>Account</TableHeaderCell>
              <TableHeaderCell>
                <span className="sr-only">Actions</span>
              </TableHeaderCell>
            </TableRow>
          </TableHead>
          <TableBody>
            {query.data.map((lecturer) => (
              <TableRow key={lecturer.staffNumber}>
                <TableCell label="Staff number" className="font-mono">
                  {lecturer.staffNumber}
                </TableCell>
                <TableCell label="Name">
                  <span className="flex flex-wrap items-center gap-2">
                    {lecturer.fullName}
                    {lecturer.leftAt && <LeftBadge leftAt={lecturer.leftAt} timeZone={timeZone} />}
                  </span>
                </TableCell>
                <TableCell label="Title">{lecturer.title}</TableCell>
                <TableCell label="Department" className="font-mono">
                  {lecturer.department}
                </TableCell>
                <TableCell label="Modules">
                  <ModuleLinks codes={lecturer.moduleCodes} />
                </TableCell>
                <TableCell label="Account">
                  {lecturer.hasAccount ? (
                    <Badge variant="success" icon={CircleCheck}>
                      Has account
                    </Badge>
                  ) : (
                    <Badge variant="neutral" icon={CircleSlash}>
                      No account
                    </Badge>
                  )}
                </TableCell>
                <TableCell className="text-right">
                  <span className="inline-flex flex-wrap justify-end gap-2">
                    <Button
                      variant="secondary"
                      size="sm"
                      aria-label={`Edit ${lecturer.staffNumber}`}
                      onClick={() => setEditing(lecturer)}
                    >
                      <Pencil aria-hidden="true" className="size-4" />
                      Edit
                    </Button>
                    {!lecturer.leftAt && (
                      <Button
                        variant="secondary"
                        size="sm"
                        aria-label={`Mark ${lecturer.staffNumber} as left`}
                        onClick={() => setLeaving(lecturer)}
                      >
                        <LogOut aria-hidden="true" className="size-4" />
                        Mark as left
                      </Button>
                    )}
                  </span>
                </TableCell>
              </TableRow>
            ))}
          </TableBody>
        </Table>
      </Refetching>
    )
  }

  return (
    <>
      <PageHeader
        title="Lecturers"
        description="Teaching staff, their modules and whether they can sign in."
        actions={
          <Button onClick={() => setCreateOpen(true)}>
            <UserPlus aria-hidden="true" className="size-4" />
            Create lecturer
          </Button>
        }
      />
      <SearchInput
        label="Search lecturers"
        showLabel
        value={values.q}
        onChange={(next) => update({ q: next })}
        placeholder="Staff number or name"
        className="mb-4 w-full sm:w-80"
      />
      {content}
      <CreateLecturerDialog open={createOpen} onOpenChange={setCreateOpen} />
      <EditLecturerDialog lecturer={editing} onOpenChange={(open) => !open && setEditing(null)} />
      <MarkLecturerLeftDialog
        lecturer={leaving}
        onOpenChange={(open) => !open && setLeaving(null)}
      />
    </>
  )
}
