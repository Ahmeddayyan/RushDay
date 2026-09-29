import { useState } from 'react'
import { zodResolver } from '@hookform/resolvers/zod'
import { CalendarClock, Hourglass, Pin, Plus, SquarePen, Trash2 } from 'lucide-react'
import { useForm, useWatch } from 'react-hook-form'
import { z } from 'zod'

import { describeProblem } from '@/api/problem'
import type { AnnouncementView } from '@/api/types/common'
import type { ModuleAnnouncementRequest } from '@/api/types/lecturer'
import {
  AlertDialog,
  AlertDialogAction,
  AlertDialogCancel,
  AlertDialogContent,
  AlertDialogFooter,
  AlertDialogTrigger,
  Badge,
  Button,
  Card,
  Checkbox,
  Dialog,
  DialogContent,
  DialogFooter,
  DialogTrigger,
  EmptyState,
  ErrorState,
  FormError,
  FormField,
  Input,
  LoadingRegion,
  SkeletonText,
  Textarea,
} from '@/components/ui'
import { formatDateTime } from '@/lib/format'
import { toast } from '@/lib/toast'
import { useDocumentTitle } from '@/lib/useDocumentTitle'

import {
  useCreateModuleAnnouncement,
  useDeleteModuleAnnouncement,
  useModuleAnnouncements,
  useUpdateModuleAnnouncement,
} from './hooks/useModuleAnnouncements'
import { useModuleContext } from './ModulePage'

const announcementSchema = z.object({
  title: z.string().trim().min(1, 'Enter a title.').max(120, 'Titles are at most 120 characters.'),
  body: z
    .string()
    .trim()
    .min(1, 'Enter the announcement text.')
    .max(4000, 'The body is at most 4,000 characters.'),
  pinned: z.boolean(),
  publishedAt: z.string(),
  expiresAt: z.string(),
})

type AnnouncementFormValues = z.infer<typeof announcementSchema>

const BODY_MAX = 4000

function toDatetimeLocal(iso: string | null | undefined): string {
  if (!iso) return ''
  const date = new Date(iso)
  if (Number.isNaN(date.getTime())) return ''
  const pad = (n: number) => String(n).padStart(2, '0')
  return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}T${pad(date.getHours())}:${pad(date.getMinutes())}`
}

function fromDatetimeLocal(value: string): string | undefined {
  if (!value) return undefined
  const date = new Date(value)
  return Number.isNaN(date.getTime()) ? undefined : date.toISOString()
}

function toRequest(values: AnnouncementFormValues): ModuleAnnouncementRequest {
  const request: ModuleAnnouncementRequest = { title: values.title, body: values.body, pinned: values.pinned }
  const publishedAt = fromDatetimeLocal(values.publishedAt)
  const expiresAt = fromDatetimeLocal(values.expiresAt)
  if (publishedAt) request.publishedAt = publishedAt
  if (expiresAt) request.expiresAt = expiresAt
  return request
}

interface AnnouncementFormProps {
  announcement?: AnnouncementView
  onSaved: () => void
  onCancel: () => void
  onSubmit: (request: ModuleAnnouncementRequest) => Promise<AnnouncementView>
}

function AnnouncementForm({ announcement, onSaved, onCancel, onSubmit }: AnnouncementFormProps) {
  const [formError, setFormError] = useState<string | null>(null)
  const {
    register,
    handleSubmit,
    control,
    formState: { errors, isSubmitting },
  } = useForm<AnnouncementFormValues>({
    resolver: zodResolver(announcementSchema),
    defaultValues: {
      title: announcement?.title ?? '',
      body: announcement?.body ?? '',
      pinned: announcement?.pinned ?? false,
      publishedAt: toDatetimeLocal(announcement?.publishedAt),
      expiresAt: toDatetimeLocal(announcement?.expiresAt),
    },
  })
  const bodyValue = useWatch({ control, name: 'body' })
  const bodyLength = bodyValue?.length ?? 0

  const submit = handleSubmit(async (values) => {
    setFormError(null)
    try {
      await onSubmit(toRequest(values))
      onSaved()
    } catch (error) {
      setFormError(describeProblem(error).message)
    }
  })

  return (
    <form onSubmit={(event) => void submit(event)} noValidate className="flex flex-col gap-4">
      <FormField label="Title" error={errors.title?.message} required>
        <Input {...register('title')} maxLength={120} />
      </FormField>
      <FormField
        label="Announcement text"
        error={errors.body?.message}
        required
        hint={`${bodyLength}/${BODY_MAX} characters`}
      >
        <Textarea {...register('body')} rows={5} maxLength={BODY_MAX} />
      </FormField>
      <Checkbox label="Pin to the top of the list" {...register('pinned')} />
      <div className="grid gap-4 sm:grid-cols-2">
        <FormField label="Publish at" hint="Leave blank to publish immediately.">
          <Input type="datetime-local" {...register('publishedAt')} />
        </FormField>
        <FormField label="Expires at" hint="Leave blank to never expire.">
          <Input type="datetime-local" {...register('expiresAt')} />
        </FormField>
      </div>
      <FormError>{formError}</FormError>
      <DialogFooter>
        <Button type="button" variant="secondary" onClick={onCancel} disabled={isSubmitting}>
          Cancel
        </Button>
        <Button type="submit" loading={isSubmitting}>
          {announcement ? 'Save changes' : 'Post announcement'}
        </Button>
      </DialogFooter>
    </form>
  )
}

function statusBadge(announcement: AnnouncementView) {
  const now = Date.now()
  if (Date.parse(announcement.publishedAt) > now) {
    return (
      <Badge variant="info" icon={Hourglass}>
        Publishes {formatDateTime(announcement.publishedAt)}
      </Badge>
    )
  }
  if (announcement.expiresAt && Date.parse(announcement.expiresAt) <= now) {
    return (
      <Badge variant="neutral" icon={CalendarClock}>
        Expired {formatDateTime(announcement.expiresAt)}
      </Badge>
    )
  }
  return null
}

function AnnouncementCard({
  code,
  announcement,
}: {
  code: string
  announcement: AnnouncementView
}) {
  const [editOpen, setEditOpen] = useState(false)
  const [deleteError, setDeleteError] = useState<string | null>(null)
  const updateMutation = useUpdateModuleAnnouncement(code)
  const deleteMutation = useDeleteModuleAnnouncement(code)

  async function handleDelete() {
    setDeleteError(null)
    try {
      await deleteMutation.mutateAsync(announcement.id)
      toast.success('Announcement deleted.')
    } catch (error) {
      setDeleteError(describeProblem(error).message)
    }
  }

  return (
    <Card>
      <div className="flex flex-col gap-3">
        <div className="flex flex-wrap items-center gap-2">
          {announcement.pinned && (
            <Badge variant="warning" icon={Pin}>
              Pinned
            </Badge>
          )}
          {statusBadge(announcement)}
        </div>
        <h3 className="text-lg leading-snug font-semibold text-text">{announcement.title}</h3>
        <p className="text-sm leading-relaxed whitespace-pre-line text-text">{announcement.body}</p>
        <div className="flex flex-wrap items-center gap-2">
          <Dialog open={editOpen} onOpenChange={setEditOpen}>
            <DialogTrigger asChild>
              <Button variant="secondary" size="sm">
                <SquarePen aria-hidden="true" className="size-4" />
                Edit
              </Button>
            </DialogTrigger>
            <DialogContent title="Edit announcement">
              <AnnouncementForm
                announcement={announcement}
                onCancel={() => setEditOpen(false)}
                onSaved={() => {
                  setEditOpen(false)
                  toast.success('Announcement updated.')
                }}
                onSubmit={(request) => updateMutation.mutateAsync({ id: announcement.id, body: request })}
              />
            </DialogContent>
          </Dialog>
          <AlertDialog>
            <AlertDialogTrigger asChild>
              <Button variant="danger" size="sm">
                <Trash2 aria-hidden="true" className="size-4" />
                Delete
              </Button>
            </AlertDialogTrigger>
            <AlertDialogContent
              title="Delete this announcement?"
              description={`"${announcement.title}" will be removed immediately. This can't be undone.`}
            >
              <FormError>{deleteError}</FormError>
              <AlertDialogFooter>
                <AlertDialogCancel />
                <AlertDialogAction onClick={() => void handleDelete()}>Delete</AlertDialogAction>
              </AlertDialogFooter>
            </AlertDialogContent>
          </AlertDialog>
        </div>
      </div>
    </Card>
  )
}

/** `/lecturer/modules/:code/announcements` (05-frontend.md section 10): create, edit, delete. */
export function Component() {
  const { module } = useModuleContext()
  useDocumentTitle(`${module.code} announcements · RushDay`)
  const query = useModuleAnnouncements(module.code)
  const createMutation = useCreateModuleAnnouncement(module.code)
  // Radix unmounts DialogContent on close by default, so a fresh AnnouncementForm (and fresh
  // defaultValues) mounts every time the dialog opens; no key trick is needed.
  const [createOpen, setCreateOpen] = useState(false)

  let content
  if (query.isPending) {
    content = (
      <LoadingRegion label="announcements">
        <SkeletonText lines={3} />
      </LoadingRegion>
    )
  } else if (query.isError) {
    content = <ErrorState error={query.error} onRetry={() => void query.refetch()} />
  } else if (query.data.length === 0) {
    content = (
      <EmptyState
        title="No announcements for this module yet."
        description="Post one to let your students know about a change."
      />
    )
  } else {
    content = (
      <div className="flex flex-col gap-4">
        {query.data.map((announcement) => (
          <AnnouncementCard key={announcement.id} code={module.code} announcement={announcement} />
        ))}
      </div>
    )
  }

  return (
    <div className="flex flex-col gap-4">
      <div className="flex justify-end">
        <Dialog open={createOpen} onOpenChange={setCreateOpen}>
          <DialogTrigger asChild>
            <Button>
              <Plus aria-hidden="true" className="size-4" />
              New announcement
            </Button>
          </DialogTrigger>
          <DialogContent title="New announcement">
            <AnnouncementForm
              onCancel={() => setCreateOpen(false)}
              onSaved={() => {
                setCreateOpen(false)
                toast.success('Announcement posted.')
              }}
              onSubmit={(request) => createMutation.mutateAsync(request)}
            />
          </DialogContent>
        </Dialog>
      </div>
      {content}
    </div>
  )
}
