import { useState } from 'react'
import {
  BookOpen,
  CalendarClock,
  CalendarX,
  Megaphone,
  Pencil,
  Pin,
  Plus,
  School,
  Trash2,
} from 'lucide-react'

import type { AnnouncementView } from '@/api/types/common'
import {
  Badge,
  Button,
  Card,
  EmptyState,
  ErrorState,
  LoadingRegion,
  PageHeader,
  Refetching,
  Skeleton,
  SkeletonText,
} from '@/components/ui'
import { toast } from '@/lib/toast'
import { useDocumentTitle } from '@/lib/useDocumentTitle'

import { AnnouncementDialog } from './components/AnnouncementDialog'
import { ConfirmDialog } from './components/ConfirmDialog'
import { Timestamp } from './components/Timestamp'
import { useAdminAnnouncements, useDeleteAnnouncement } from './hooks/useAdminAnnouncements'
import { useInstitutionClock } from './lib/useInstitutionClock'
import { useOneShotFlag } from './lib/urlState'
import { currentTime } from './lib/zonedTime'

function AnnouncementItem({
  announcement,
  timeZone,
  mountedAt,
  onEdit,
  onDelete,
}: {
  announcement: AnnouncementView
  timeZone: string
  mountedAt: number
  onEdit: () => void
  onDelete: () => void
}) {
  const headingId = `admin-announcement-${announcement.id}`
  const future = Date.parse(announcement.publishedAt) > mountedAt
  const expired = announcement.expiresAt !== null && Date.parse(announcement.expiresAt) <= mountedAt
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
          {future && (
            <Badge variant="info" icon={CalendarClock}>
              Not published yet
            </Badge>
          )}
          {expired && (
            <Badge variant="neutral" icon={CalendarX}>
              Expired
            </Badge>
          )}
        </div>
        <h2 id={headingId} className="text-lg leading-snug font-semibold text-text">
          {announcement.title}
        </h2>
        <p className="text-sm leading-relaxed whitespace-pre-line text-text">{announcement.body}</p>
        <dl className="flex flex-wrap gap-x-6 gap-y-1 text-sm text-muted">
          <div className="flex gap-1.5">
            <dt>By</dt>
            <dd className="text-text">{announcement.author}</dd>
          </div>
          <div className="flex gap-1.5">
            <dt>{future ? 'Publishes' : 'Published'}</dt>
            <dd className="text-text">
              <Timestamp iso={announcement.publishedAt} timeZone={timeZone} zone />
            </dd>
          </div>
          {announcement.expiresAt && (
            <div className="flex gap-1.5">
              <dt>{expired ? 'Expired' : 'Expires'}</dt>
              <dd className="text-text">
                <Timestamp iso={announcement.expiresAt} timeZone={timeZone} zone />
              </dd>
            </div>
          )}
        </dl>
        <div className="flex flex-wrap gap-2">
          <Button
            variant="secondary"
            size="sm"
            onClick={onEdit}
            aria-label={`Edit ${announcement.title}`}
          >
            <Pencil aria-hidden="true" className="size-4" />
            Edit
          </Button>
          <Button
            variant="ghost"
            size="sm"
            className="text-danger"
            onClick={onDelete}
            aria-label={`Delete ${announcement.title}`}
          >
            <Trash2 aria-hidden="true" className="size-4" />
            Delete
          </Button>
        </div>
      </article>
    </Card>
  )
}

/**
 * `/admin/announcements` (05-frontend.md section 10): every announcement of every scope, including
 * future and expired ones; New (university scope), Edit and Delete.
 */
export function Component() {
  useDocumentTitle('Announcements · Administration · RushDay')
  const { timeZone } = useInstitutionClock()
  const query = useAdminAnnouncements()
  const remove = useDeleteAnnouncement()
  const [mountedAt] = useState(currentTime)
  // The overview's "New announcement" quick action arrives as ?new=1 and opens the dialog once.
  const openNew = useOneShotFlag('new')
  const [dialog, setDialog] = useState<{ announcement: AnnouncementView | null } | null>(
    openNew ? { announcement: null } : null,
  )
  const [deleting, setDeleting] = useState<AnnouncementView | null>(null)

  let content
  if (query.isPending) {
    content = (
      <LoadingRegion label="announcements">
        <div className="flex flex-col gap-4">
          {[0, 1, 2].map((key) => (
            <div key={key} className="rounded-lg border border-border bg-surface p-6">
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
          description="University announcements reach every student and lecturer."
          action={
            <Button onClick={() => setDialog({ announcement: null })}>New announcement</Button>
          }
        />
      </Card>
    )
  } else {
    content = (
      <Refetching active={query.isFetching}>
        <div className="flex flex-col gap-4">
          {query.data.map((announcement) => (
            <AnnouncementItem
              key={announcement.id}
              announcement={announcement}
              timeZone={timeZone}
              mountedAt={mountedAt}
              onEdit={() => setDialog({ announcement })}
              onDelete={() => setDeleting(announcement)}
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
        description="University announcements, and every module's, including scheduled and expired ones."
        actions={
          <Button onClick={() => setDialog({ announcement: null })}>
            <Plus aria-hidden="true" className="size-4" />
            New announcement
          </Button>
        }
      />
      {content}
      <AnnouncementDialog
        key={dialog?.announcement?.id ?? 'new'}
        open={dialog !== null}
        onOpenChange={(open) => !open && setDialog(null)}
        announcement={dialog?.announcement ?? null}
        timeZone={timeZone}
      />
      <ConfirmDialog
        open={deleting !== null}
        onOpenChange={(open) => !open && setDeleting(null)}
        title={`Delete "${deleting?.title ?? ''}"?`}
        description="It disappears for everyone straight away. The deletion is recorded in the audit log."
        confirmLabel="Delete announcement"
        onConfirm={async () => {
          if (!deleting) return
          await remove.mutateAsync(deleting.id)
          toast.success('Announcement deleted.')
        }}
      />
    </div>
  )
}
