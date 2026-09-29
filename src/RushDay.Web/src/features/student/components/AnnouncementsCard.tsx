import { ArrowRight, Megaphone, Pin } from 'lucide-react'
import { Link } from 'react-router'

import type { AnnouncementView } from '@/api/types/common'
import { Badge, Card, CardHeader, CardTitle, EmptyState } from '@/components/ui'
import { formatShortDate } from '@/lib/format'

export interface AnnouncementsCardProps {
  announcements: readonly AnnouncementView[]
  timeZone: string
}

/** The dashboard's latest three announcements, pinned first (05-frontend.md section 10, `/student`). */
export function AnnouncementsCard({ announcements, timeZone }: AnnouncementsCardProps) {
  const latest = announcements.slice(0, 3)

  return (
    <Card>
      <CardHeader
        actions={
          <Link
            to="/announcements"
            className="inline-flex items-center gap-1 rounded-sm text-sm font-medium text-primary hover:underline"
          >
            All announcements
            <ArrowRight aria-hidden="true" className="size-4" />
          </Link>
        }
      >
        <CardTitle>Announcements</CardTitle>
      </CardHeader>
      {latest.length === 0 ? (
        <EmptyState
          compact
          icon={Megaphone}
          headingLevel="p"
          title="No announcements yet."
          description="Notices from the university and your modules appear here."
        />
      ) : (
        <ul className="divide-y divide-border">
          {latest.map((announcement) => (
            <li key={announcement.id} className="py-3 first:pt-0 last:pb-0">
              <article aria-labelledby={`dashboard-announcement-${announcement.id}`}>
                <div className="mb-1 flex flex-wrap items-center gap-2">
                  {announcement.pinned && (
                    <Badge variant="warning" icon={Pin}>
                      Pinned
                    </Badge>
                  )}
                  {announcement.scope === 'module' && announcement.moduleCode && (
                    <Badge variant="neutral" className="font-mono">
                      {announcement.moduleCode}
                    </Badge>
                  )}
                  <time dateTime={announcement.publishedAt} className="text-xs text-muted">
                    {formatShortDate(announcement.publishedAt, timeZone)}
                  </time>
                </div>
                <h3
                  id={`dashboard-announcement-${announcement.id}`}
                  className="text-sm font-semibold text-text"
                >
                  {announcement.title}
                </h3>
                <p className="mt-0.5 line-clamp-2 text-sm whitespace-pre-line text-muted">
                  {announcement.body}
                </p>
              </article>
            </li>
          ))}
        </ul>
      )}
    </Card>
  )
}
