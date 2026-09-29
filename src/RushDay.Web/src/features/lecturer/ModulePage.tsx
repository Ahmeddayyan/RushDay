import { Outlet, useOutletContext, useParams } from 'react-router'

import { usePublicStatus } from '@/api/endpoints/public'
import type { LecturerModuleSummary } from '@/api/types/lecturer'
import {
  Badge,
  ErrorState,
  LoadingRegion,
  MarksStatusChip,
  PageHeader,
  Skeleton,
  TabNav,
} from '@/components/ui'
import { formatSemester } from '@/lib/format'
import { useDocumentTitle } from '@/lib/useDocumentTitle'

import { useMyModules } from './hooks/useMyModules'

export interface ModuleOutletContext {
  module: LecturerModuleSummary
  timeZone: string | undefined
}

/** Reads the module and time zone `ModulePage` puts on its outlet context. */
// The page and its hook belong together; a full reload on edit is an acceptable trade.
// eslint-disable-next-line react-refresh/only-export-components
export function useModuleContext(): ModuleOutletContext {
  return useOutletContext<ModuleOutletContext>()
}

/**
 * `/lecturer/modules/:code` (05-frontend.md section 10): the module header (found in
 * `['lecturer','modules']`, per section 6.2) and the Roster | Marks | Announcements tabs.
 */
export function Component() {
  const { code = '' } = useParams()
  const modulesQuery = useMyModules()
  const { data: status } = usePublicStatus()
  const timeZone = status?.institution.timeZone
  const module = modulesQuery.data?.find((item) => item.code === code)

  useDocumentTitle(`${code} · RushDay`)

  if (modulesQuery.isPending) {
    return (
      <LoadingRegion label={`${code}`}>
        <Skeleton className="mb-4 h-8 w-64" />
        <Skeleton className="h-10 w-full" />
      </LoadingRegion>
    )
  }
  if (modulesQuery.isError) {
    return <ErrorState error={modulesQuery.error} onRetry={() => void modulesQuery.refetch()} />
  }
  if (!module) {
    return (
      <ErrorState
        title="This module isn't assigned to you."
        description="Check the code, or ask an administrator to assign you."
      />
    )
  }

  return (
    <div>
      <PageHeader
        eyebrow="Lecturer"
        title={`${module.code} · ${module.title}`}
        actions={
          <MarksStatusChip
            status={module.marks.status}
            publishedAt={module.marks.publishedAt}
            {...(timeZone ? { timeZone } : {})}
          />
        }
      >
        <div className="flex flex-wrap items-center gap-2">
          <Badge>{module.credits} credits</Badge>
          <Badge>{formatSemester(module.semester)}</Badge>
          <Badge className="capitalize">{module.myRole}</Badge>
        </div>
      </PageHeader>
      <TabNav
        label={`${module.code} sections`}
        items={[
          { to: `/lecturer/modules/${code}`, label: 'Roster', end: true },
          { to: `/lecturer/modules/${code}/marks`, label: 'Marks' },
          { to: `/lecturer/modules/${code}/announcements`, label: 'Announcements' },
        ]}
      />
      <div className="mt-6">
        <Outlet context={{ module, timeZone } satisfies ModuleOutletContext} />
      </div>
    </div>
  )
}
