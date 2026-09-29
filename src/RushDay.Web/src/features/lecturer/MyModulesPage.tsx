import { useMemo, useState } from 'react'

import { usePublicStatus } from '@/api/endpoints/public'
import type { LecturerModuleSummary } from '@/api/types/lecturer'
import {
  ButtonLink,
  Card,
  EmptyState,
  ErrorState,
  LoadingRegion,
  MarksStatusChip,
  PageHeader,
  Refetching,
  Skeleton,
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeaderCell,
  TableRow,
} from '@/components/ui'
import { formatSemester } from '@/lib/format'
import { useDocumentTitle } from '@/lib/useDocumentTitle'

import { useMyModules } from './hooks/useMyModules'

type SortKey = 'code' | 'title' | 'semester'

const EMPTY_MODULES: LecturerModuleSummary[] = []

/** `/lecturer/modules` (05-frontend.md section 10): every module the lecturer teaches, client-sorted. */
export function Component() {
  useDocumentTitle('My modules · RushDay')
  const modulesQuery = useMyModules()
  const { data: status } = usePublicStatus()
  const timeZone = status?.institution.timeZone
  const [sort, setSort] = useState<{ key: SortKey; direction: 'ascending' | 'descending' }>({
    key: 'code',
    direction: 'ascending',
  })

  const modules = modulesQuery.data ?? EMPTY_MODULES
  const sorted = useMemo(() => {
    const copy = [...modules]
    copy.sort((a, b) => {
      const result = a[sort.key].localeCompare(b[sort.key])
      return sort.direction === 'ascending' ? result : -result
    })
    return copy
  }, [modules, sort])

  function toggleSort(key: SortKey) {
    setSort((prev) =>
      prev.key === key
        ? { key, direction: prev.direction === 'ascending' ? 'descending' : 'ascending' }
        : { key, direction: 'ascending' },
    )
  }

  function sortStateFor(key: SortKey) {
    return sort.key === key ? sort.direction : 'none'
  }

  let content
  if (modulesQuery.isPending) {
    content = (
      <LoadingRegion label="your modules">
        <div className="space-y-2 p-4">
          {[0, 1, 2].map((key) => (
            <Skeleton key={key} className="h-12 w-full" />
          ))}
        </div>
      </LoadingRegion>
    )
  } else if (modulesQuery.isError) {
    content = <ErrorState error={modulesQuery.error} onRetry={() => void modulesQuery.refetch()} />
  } else if (sorted.length === 0) {
    content = (
      <EmptyState
        title="No modules are assigned to you. Ask an administrator."
        compact
      />
    )
  } else {
    content = (
      <Refetching active={modulesQuery.isFetching}>
        <Table caption="Your modules">
          <TableHead>
            <TableRow>
              <TableHeaderCell sort={sortStateFor('code')} onSort={() => toggleSort('code')}>
                Code
              </TableHeaderCell>
              <TableHeaderCell sort={sortStateFor('title')} onSort={() => toggleSort('title')}>
                Title
              </TableHeaderCell>
              <TableHeaderCell sort={sortStateFor('semester')} onSort={() => toggleSort('semester')}>
                Semester
              </TableHeaderCell>
              <TableHeaderCell numeric>Enrolled</TableHeaderCell>
              <TableHeaderCell>Marks</TableHeaderCell>
              <TableHeaderCell numeric>Entered / missing</TableHeaderCell>
              <TableHeaderCell>My role</TableHeaderCell>
              <TableHeaderCell>Actions</TableHeaderCell>
            </TableRow>
          </TableHead>
          <TableBody>
            {sorted.map((module: LecturerModuleSummary) => (
              <TableRow key={module.code}>
                <TableCell label="Code" className="font-mono">
                  {module.code}
                </TableCell>
                <TableCell label="Title">{module.title}</TableCell>
                <TableCell label="Semester">{formatSemester(module.semester)}</TableCell>
                <TableCell label="Enrolled" numeric>
                  {module.enrolledCount} / {module.capacity}
                </TableCell>
                <TableCell label="Marks">
                  <MarksStatusChip
                    status={module.marks.status}
                    publishedAt={module.marks.publishedAt}
                    {...(timeZone ? { timeZone } : {})}
                  />
                </TableCell>
                <TableCell label="Entered / missing" numeric>
                  {module.marks.entered} / {module.marks.missing}
                </TableCell>
                <TableCell label="My role" className="capitalize">
                  {module.myRole}
                </TableCell>
                <TableCell label="Actions">
                  <div className="flex flex-wrap gap-x-3 gap-y-1">
                    <ButtonLink to={`/lecturer/modules/${module.code}`} variant="ghost" size="sm">
                      Roster
                    </ButtonLink>
                    <ButtonLink to={`/lecturer/modules/${module.code}/marks`} variant="ghost" size="sm">
                      Marks
                    </ButtonLink>
                    <ButtonLink
                      to={`/lecturer/modules/${module.code}/announcements`}
                      variant="ghost"
                      size="sm"
                    >
                      Announcements
                    </ButtonLink>
                  </div>
                </TableCell>
              </TableRow>
            ))}
          </TableBody>
        </Table>
      </Refetching>
    )
  }

  return (
    <div>
      <PageHeader title="My modules" description="Every module you're assigned to teach this year." />
      <Card flush>{content}</Card>
    </div>
  )
}
