import { BookOpen, Megaphone, Pin, School } from 'lucide-react'

import { useAnnouncements } from '@/api/endpoints/announcements'
import { usePublicStatus } from '@/api/endpoints/public'
import type { AnnouncementView } from '@/api/types/common'
import {
  Badge,
  Card,
  EmptyState,
  ErrorState,
  LoadingRegion,
  PageHeader,
  Refetching,
  Skeleton,
  SkeletonText,
  Tooltip,
} from '@/components/ui'
import { DEFAULT_TIME_ZONE, formatDate, formatRelative } from '@/lib/format'
import { useDocumentTitle } from '@/lib/useDocumentTitle'

function AnnouncementCard({
  announcement,
  timeZone,
}: {
  announcement: AnnouncementView
  timeZone: string
}) {
  const headingId = `announcement-${announcement.id}`
  return (
    <Card>
      <article aria-labelledby={headingId} className="flex flex-col gap-3">
        <div className="flex flex-wrap items-center gap-2">
          {announcement.pinned && (
            <Badge variant="warning" icon={Pin}>
              Pinned
            </Badge>
          )}
          {announcement.scope === 'university' ? (
            <Badge variant="info" icon={School}>
              University
            </Badge>
          ) : (
            <Badge variant="neutral" icon={BookOpen} className="font-mono">
              {announcement.moduleCode ?? 'Module'}
            </Badge>
          )}
          <Tooltip content={formatRelative(announcement.publishedAt)}>
            <time
              dateTime={announcement.publishedAt}
              tabIndex={0}
              className="ml-auto rounded-sm text-sm text-muted"
            >
              {formatDate(announcement.publishedAt, timeZone)}
            </time>
          </Tooltip>
        </div>
        <h2 id={headingId} className="text-lg leading-snug font-semibold text-text">
          {announcement.title}
        </h2>
        <p className="text-sm leading-relaxed whitespace-pre-line text-text">{announcement.body}</p>
        <p className="text-sm text-muted">Posted by {announcement.author}</p>
      </article>
    </Card>
  )
}

/** `/announcements` (05-frontend.md section 10): what the signed-in person can see now, pinned first. */
export function Component() {
  useDocumentTitle('Announcements · RushDay')
  const query = useAnnouncements()
  const { data: status } = usePublicStatus()
  const timeZone = status?.institution.timeZone ?? DEFAULT_TIME_ZONE

  let content
  if (query.isPending) {
    content = (
      <LoadingRegion label="announcements">
        <div className="flex flex-col gap-4">
          {[0, 1, 2].map((key) => (
            <div key={key} className="rounded-lg border border-border bg-surface p-4 md:p-6">
              <Skeleton className="mb-3 h-5 w-24 rounded-full" />
              <Skeleton className="mb-3 h-6 w-2/3" />
              <SkeletonText lines={2} />
            </div>
          ))}
        </div>
      </LoadingRegion>
    )
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
          icon={Megaphone}
          title="No announcements yet."
          description="Notices from the university and from your modules appear here when they are published."
        />
      </Card>
    )
  } else {
    // The server already sorts pinned first, then newest; keep that order.
    content = (
      <Refetching active={query.isFetching}>
        <div className="flex flex-col gap-4">
          {query.data.map((announcement) => (
            <AnnouncementCard
              key={announcement.id}
              announcement={announcement}
              timeZone={timeZone}
            />
          ))}
        </div>
      </Refetching>
    )
  }

  return (
    <div className="max-w-3xl">
      <PageHeader
        title="Announcements"
        description="Notices from the university and from your modules."
      />
      {content}
    </div>
  )
}
