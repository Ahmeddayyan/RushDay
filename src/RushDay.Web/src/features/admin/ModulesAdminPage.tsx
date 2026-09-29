import { useState } from 'react'
import { BookOpen, CircleCheck, CircleSlash, Plus } from 'lucide-react'
import { Link } from 'react-router'

import type { AdminModule } from '@/api/types/admin'
import {
  Badge,
  Button,
  Card,
  Checkbox,
  EmptyState,
  ErrorState,
  FormField,
  MarksStatusChip,
  PageHeader,
  Refetching,
  SearchInput,
  Select,
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeaderCell,
  TableRow,
  type SortState,
} from '@/components/ui'
import { formatNumber, formatSemester } from '@/lib/format'
import { useDocumentTitle } from '@/lib/useDocumentTitle'

import { ModuleEditDialog } from './components/ModuleEditDialog'
import { TableSkeleton } from './components/TableSkeleton'
import { useAdminModules } from './hooks/useModules'
import { useInstitutionClock } from './lib/useInstitutionClock'
import { useUrlFilters } from './lib/urlState'

const FILTERS = ['q', 'semester', 'inactive'] as const

type SortKey = 'code' | 'title' | 'credits' | 'capacity' | 'enrolled'

const SEMESTER_FILTER = [
  { value: '', label: 'Both semesters' },
  { value: 'autumn', label: 'Autumn' },
  { value: 'spring', label: 'Spring' },
]

function leaderOf(module: AdminModule): string {
  const leader = module.lecturers.find((lecturer) => lecturer.role === 'leader')
  return leader ? `${leader.title} ${leader.fullName}` : ''
}

function compare(a: AdminModule, b: AdminModule, key: SortKey): number {
  switch (key) {
    case 'title':
      return a.title.localeCompare(b.title, 'en-GB')
    case 'credits':
      return a.credits - b.credits
    case 'capacity':
      return a.capacity - b.capacity
    case 'enrolled':
      return a.enrolledCount - b.enrolledCount
    default:
      return a.code.localeCompare(b.code, 'en-GB')
  }
}

/**
 * `/admin/modules` (05-frontend.md section 10): every module (about 121 rows) filtered and sorted
 * in the browser, with "New module" and Edit through `ModuleEditDialog`.
 */
export function Component() {
  useDocumentTitle('Modules · RushDay')
  const { timeZone } = useInstitutionClock()
  const { values, update } = useUrlFilters(FILTERS)
  const includeInactive = values.inactive === '1'
  const query = useAdminModules(includeInactive)
  const [sort, setSort] = useState<{ key: SortKey; direction: 'ascending' | 'descending' }>({
    key: 'code',
    direction: 'ascending',
  })
  const [editing, setEditing] = useState<AdminModule | null>(null)
  const [dialogOpen, setDialogOpen] = useState(false)

  function sortState(key: SortKey): SortState {
    return sort.key === key ? sort.direction : 'none'
  }

  function toggleSort(key: SortKey) {
    setSort((current) =>
      current.key === key
        ? { key, direction: current.direction === 'ascending' ? 'descending' : 'ascending' }
        : { key, direction: 'ascending' },
    )
  }

  const needle = values.q.trim().toLowerCase()
  const rows = (query.data ?? [])
    .filter(
      (module) =>
        (needle === '' ||
          module.code.toLowerCase().includes(needle) ||
          module.title.toLowerCase().includes(needle)) &&
        (values.semester === '' || module.semester === values.semester),
    )
    .sort((a, b) => compare(a, b, sort.key) * (sort.direction === 'ascending' ? 1 : -1))

  let content
  if (query.isPending) {
    content = <TableSkeleton label="modules" rows={10} />
  } else if (query.isError) {
    content = (
      <Card>
        <ErrorState error={query.error} onRetry={() => void query.refetch()} />
      </Card>
    )
  } else if (rows.length === 0) {
    content = (
      <Card>
        <EmptyState
          icon={BookOpen}
          title="No modules match these filters."
          description="Clear the search, or create a module."
          action={
            <Button variant="secondary" onClick={() => update({ q: null, semester: null })}>
              Clear filters
            </Button>
          }
        />
      </Card>
    )
  } else {
    content = (
      <Refetching active={query.isPlaceholderData}>
        <Table caption="Modules" captionHidden>
          <TableHead>
            <TableRow>
              <TableHeaderCell sort={sortState('code')} onSort={() => toggleSort('code')}>
                Code
              </TableHeaderCell>
              <TableHeaderCell sort={sortState('title')} onSort={() => toggleSort('title')}>
                Title
              </TableHeaderCell>
              <TableHeaderCell>Semester</TableHeaderCell>
              <TableHeaderCell
                numeric
                sort={sortState('credits')}
                onSort={() => toggleSort('credits')}
              >
                Credits
              </TableHeaderCell>
              <TableHeaderCell
                numeric
                sort={sortState('capacity')}
                onSort={() => toggleSort('capacity')}
              >
                Capacity
              </TableHeaderCell>
              <TableHeaderCell
                numeric
                sort={sortState('enrolled')}
                onSort={() => toggleSort('enrolled')}
                sortLabel="Enrolled this year"
              >
                Enrolled this year
              </TableHeaderCell>
              <TableHeaderCell>Leader</TableHeaderCell>
              <TableHeaderCell>Marks</TableHeaderCell>
              <TableHeaderCell>Active</TableHeaderCell>
              <TableHeaderCell>
                <span className="sr-only">Actions</span>
              </TableHeaderCell>
            </TableRow>
          </TableHead>
          <TableBody>
            {rows.map((module) => (
              <TableRow key={module.code}>
                <TableCell label="Code" className="font-mono">
                  <Link
                    to={`/admin/modules/${module.code}`}
                    className="text-primary underline-offset-2 hover:underline"
                  >
                    {module.code}
                  </Link>
                </TableCell>
                <TableCell label="Title">{module.title}</TableCell>
                <TableCell label="Semester">{formatSemester(module.semester)}</TableCell>
                <TableCell label="Credits" numeric>
                  {module.credits}
                </TableCell>
                <TableCell label="Capacity" numeric>
                  {formatNumber(module.capacity)}
                </TableCell>
                <TableCell label="Enrolled this year" numeric>
                  <span
                    className={
                      module.enrolledCount > module.capacity
                        ? 'font-semibold text-danger'
                        : undefined
                    }
                  >
                    {formatNumber(module.enrolledCount)}
                  </span>
                  {module.enrolledCount > module.capacity && (
                    <span className="sr-only"> (over capacity)</span>
                  )}
                </TableCell>
                <TableCell label="Leader">{leaderOf(module)}</TableCell>
                <TableCell label="Marks">
                  <MarksStatusChip
                    status={module.marks.status}
                    publishedAt={module.marks.publishedAt}
                    timeZone={timeZone}
                  />
                </TableCell>
                <TableCell label="Active">
                  {module.isActive ? (
                    <Badge variant="success" icon={CircleCheck}>
                      Active
                    </Badge>
                  ) : (
                    <Badge variant="neutral" icon={CircleSlash}>
                      Inactive
                    </Badge>
                  )}
                </TableCell>
                <TableCell className="text-right">
                  <Button
                    variant="secondary"
                    size="sm"
                    aria-label={`Edit ${module.code}`}
                    onClick={() => {
                      setEditing(module)
                      setDialogOpen(true)
                    }}
                  >
                    Edit
                  </Button>
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
        title="Modules"
        description="Capacity, lecturers and marks status for this academic year."
        actions={
          <Button
            onClick={() => {
              setEditing(null)
              setDialogOpen(true)
            }}
          >
            <Plus aria-hidden="true" className="size-4" />
            New module
          </Button>
        }
      />
      <div className="mb-4 flex flex-wrap items-end gap-3">
        <SearchInput
          label="Search modules"
          showLabel
          value={values.q}
          onChange={(next) => update({ q: next })}
          placeholder="Code or title"
          className="w-full sm:w-72"
        />
        <FormField label="Semester" className="w-full sm:w-44">
          <Select
            value={values.semester}
            onChange={(event) => update({ semester: event.target.value })}
            options={SEMESTER_FILTER}
          />
        </FormField>
        <Checkbox
          label="Show inactive modules"
          checked={includeInactive}
          onChange={(event) => update({ inactive: event.target.checked ? '1' : null })}
          className="sm:pb-2"
        />
      </div>
      {query.data && (
        <p className="mb-3 text-sm text-muted" aria-live="polite">
          {formatNumber(rows.length)} of {formatNumber(query.data.length)} modules
        </p>
      )}
      {content}
      <ModuleEditDialog
        key={editing?.code ?? 'new'}
        open={dialogOpen}
        onOpenChange={setDialogOpen}
        module={editing}
      />
    </>
  )
}
