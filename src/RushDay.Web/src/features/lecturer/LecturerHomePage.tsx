import { BookOpen, CircleCheck, ClipboardList, Megaphone, UsersRound } from 'lucide-react'

import { useAnnouncements } from '@/api/endpoints/announcements'
import { usePublicStatus } from '@/api/endpoints/public'
import {
  Card,
  CardDescription,
  CardHeader,
  CardTitle,
  EmptyState,
  ErrorState,
  LoadingRegion,
  PageHeader,
  Refetching,
  Skeleton,
  SkeletonText,
  StatTile,
} from '@/components/ui'
import { formatDate } from '@/lib/format'
import { useDocumentTitle } from '@/lib/useDocumentTitle'

import { GradingProgressRow } from './components/GradingProgress'
import { useMyModules } from './hooks/useMyModules'

/** `/lecturer` (05-frontend.md section 10): grading progress across every taught module and announcements. */
export function Component() {
  useDocumentTitle('Lecturer home · RushDay')
  const modulesQuery = useMyModules()
  const announcementsQuery = useAnnouncements()
  const { data: status } = usePublicStatus()
  const timeZone = status?.institution.timeZone

  const modules = modulesQuery.data ?? []
  const studentsThisYear = modules.reduce((sum, module) => sum + module.enrolledCount, 0)
  const marksEntered = modules.reduce((sum, module) => sum + module.marks.entered, 0)
  const marksMissing = modules.reduce((sum, module) => sum + module.marks.missing, 0)

  return (
    <div className="flex flex-col gap-8">
      <PageHeader title="Home" description="Your modules, grading progress and announcements." />

      <div className="grid grid-cols-2 gap-4 lg:grid-cols-4">
        <StatTile label="Modules taught" value={modules.length} icon={BookOpen} />
        <StatTile label="Students this year" value={studentsThisYear} icon={UsersRound} />
        <StatTile label="Marks entered" value={marksEntered} icon={CircleCheck} />
        <StatTile
          label="Marks missing"
          value={marksMissing}
          icon={ClipboardList}
          tone={marksMissing > 0 ? 'warning' : 'default'}
        />
      </div>

      <Card>
        <CardHeader>
          <CardTitle>Grading progress</CardTitle>
          <CardDescription>Where each of your modules stands, from draft to published.</CardDescription>
        </CardHeader>
        {modulesQuery.isPending ? (
          <LoadingRegion label="grading progress">
            <div className="space-y-4">
              {[0, 1].map((key) => (
                <Skeleton key={key} className="h-16 w-full" />
              ))}
            </div>
          </LoadingRegion>
        ) : modulesQuery.isError ? (
          <ErrorState error={modulesQuery.error} onRetry={() => void modulesQuery.refetch()} compact />
        ) : modules.length === 0 ? (
          <EmptyState
            title="No modules are assigned to you."
            description="Ask an administrator to assign you to a module."
            compact
          />
        ) : (
          <Refetching active={modulesQuery.isFetching} className="divide-y divide-border">
            {modules.map((module) => (
              <GradingProgressRow key={module.code} module={module} {...(timeZone ? { timeZone } : {})} />
            ))}
          </Refetching>
        )}
      </Card>

      <Card>
        <CardHeader>
          <CardTitle className="flex items-center gap-2">
            <Megaphone aria-hidden="true" className="size-5 text-muted" />
            Announcements
          </CardTitle>
        </CardHeader>
        {announcementsQuery.isPending ? (
          <LoadingRegion label="announcements">
            <SkeletonText lines={2} />
          </LoadingRegion>
        ) : announcementsQuery.isError ? (
          <ErrorState error={announcementsQuery.error} onRetry={() => void announcementsQuery.refetch()} compact />
        ) : announcementsQuery.data.length === 0 ? (
          <EmptyState title="No announcements yet." compact />
        ) : (
          <ul className="divide-y divide-border">
            {announcementsQuery.data.slice(0, 5).map((announcement) => (
              <li key={announcement.id} className="py-3 first:pt-0 last:pb-0">
                <p className="text-sm font-medium text-text">{announcement.title}</p>
                <p className="text-xs text-muted">
                  {formatDate(announcement.publishedAt, timeZone)} · {announcement.author}
                </p>
              </li>
            ))}
          </ul>
        )}
      </Card>
    </div>
  )
}
