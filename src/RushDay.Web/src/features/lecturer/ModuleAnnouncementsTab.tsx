import { useState } from 'react'
import { zodResolver } from '@hookform/resolvers/zod'
import { CalendarClock, CalendarX, Pencil, Pin, Plus, Trash2 } from 'lucide-react'
import { useForm, useWatch } from 'react-hook-form'

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
  ZonedDateTimeField,
} from '@/components/ui'
import { DEFAULT_TIME_ZONE, formatDateTime, formatNumber } from '@/lib/format'
import { toast } from '@/lib/toast'
import { useDirtyForm } from '@/lib/useDirtyForm'
import { useDocumentTitle } from '@/lib/useDocumentTitle'
import { z } from '@/lib/zod'
import { toApiInstant, toZonedInput, zonedInputToMs } from '@/lib/zonedTime'

import {
  useCreateModuleAnnouncement,
  useDeleteModuleAnnouncement,
  useModuleAnnouncements,
  useUpdateModuleAnnouncement,
} from './hooks/useModuleAnnouncements'
import { useModuleContext } from './ModulePage'

const TITLE_MAX = 120
const BODY_MAX = 4000

/**
 * Publish and expiry times are wall-clock times in the institution's zone (D26), exactly as on the
 * administrator's announcement form, whatever zone the lecturer's own computer is in.
 */
function announcementSchema(timeZone: string) {
  return z
    .object({
      title: z
        .string()
        .trim()
        .min(1, 'Enter a title.')
        .max(TITLE_MAX, `Use ${TITLE_MAX} characters or fewer.`),
      body: z
        .string()
        .trim()
        .min(1, 'Write the announcement.')
        .max(BODY_MAX, `Use ${formatNumber(BODY_MAX)} characters or fewer.`),
      pinned: z.boolean(),
      publishedAt: z.string(),
      expiresAt: z.string(),
    })
    .superRefine((values, context) => {
      const published = values.publishedAt ? zonedInputToMs(values.publishedAt, timeZone) : null
      const expires = values.expiresAt ? zonedInputToMs(values.expiresAt, timeZone) : null
      if (values.publishedAt && published === null) {
        context.addIssue({
          code: 'custom',
          path: ['publishedAt'],
          message: 'Enter a date and time.',
        })
      }
      if (values.expiresAt && expires === null) {
        context.addIssue({ code: 'custom', path: ['expiresAt'], message: 'Enter a date and time.' })
      }
      if (published !== null && expires !== null && expires <= published) {
        context.addIssue({
          code: 'custom',
          path: ['expiresAt'],
          message: 'The expiry must be after the publication time.',
        })
      }
    })
}

type AnnouncementFormValues = z.infer<ReturnType<typeof announcementSchema>>

function toRequest(values: AnnouncementFormValues, timeZone: string): ModuleAnnouncementRequest {
  const request: ModuleAnnouncementRequest = {
    title: values.title,
    body: values.body,
    pinned: values.pinned,
  }
  const published = values.publishedAt ? zonedInputToMs(values.publishedAt, timeZone) : null
  const expires = values.expiresAt ? zonedInputToMs(values.expiresAt, timeZone) : null
  if (published !== null) request.publishedAt = toApiInstant(published)
  if (expires !== null) request.expiresAt = toApiInstant(expires)
  return request
}

interface AnnouncementFormProps {
  announcement?: AnnouncementView
  timeZone: string
  onSaved: () => void
  onCancel: () => void
  onSubmit: (request: ModuleAnnouncementRequest) => Promise<AnnouncementView>
}

function AnnouncementForm({
  announcement,
  timeZone,
  onSaved,
  onCancel,
  onSubmit,
}: AnnouncementFormProps) {
  const [formError, setFormError] = useState<string | null>(null)
  const {
    register,
    handleSubmit,
    control,
    formState: { errors, isSubmitting, isDirty },
  } = useForm<AnnouncementFormValues>({
    resolver: zodResolver(announcementSchema(timeZone)),
    mode: 'onBlur',
    reValidateMode: 'onChange',
    defaultValues: {
      title: announcement?.title ?? '',
      body: announcement?.body ?? '',
      pinned: announcement?.pinned ?? false,
      publishedAt: announcement ? toZonedInput(announcement.publishedAt, timeZone) : '',
      expiresAt: announcement?.expiresAt ? toZonedInput(announcement.expiresAt, timeZone) : '',
    },
  })
  // A session that expires mid-edit re-authenticates in place instead of losing the text.
  useDirtyForm(isDirty)
  const body = useWatch({ control, name: 'body' }) ?? ''
  const publishedAt = useWatch({ control, name: 'publishedAt' }) ?? ''
  const expiresAt = useWatch({ control, name: 'expiresAt' }) ?? ''

  const submit = handleSubmit(async (values) => {
    setFormError(null)
    try {
      await onSubmit(toRequest(values, timeZone))
      onSaved()
    } catch (error) {
      setFormError(describeProblem(error).message)
    }
  })

  return (
    <form onSubmit={(event) => void submit(event)} noValidate className="flex flex-col gap-4">
      <FormField
        label="Title"
        error={errors.title?.message}
        required
        hint={`At most ${TITLE_MAX} characters.`}
      >
        <Input {...register('title')} maxLength={TITLE_MAX} />
      </FormField>
      <FormField
        label="Message"
        error={errors.body?.message}
        required
        hint={`${formatNumber(body.length)} of ${formatNumber(BODY_MAX)} characters. Plain text; line breaks are kept.`}
      >
        <Textarea {...register('body')} rows={5} maxLength={BODY_MAX} />
      </FormField>
      <Checkbox label="Pin to the top" {...register('pinned')} />
      <div className="grid gap-4 sm:grid-cols-2">
        <ZonedDateTimeField
          label="Publish at"
          timeZone={timeZone}
          value={publishedAt}
          error={errors.publishedAt?.message}
          hint="Optional. Empty publishes now."
          inputProps={register('publishedAt')}
        />
        <ZonedDateTimeField
          label="Expires at"
          timeZone={timeZone}
          value={expiresAt}
          error={errors.expiresAt?.message}
          hint="Optional."
          inputProps={register('expiresAt')}
        />
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

function AnnouncementCard({
  code,
  announcement,
  timeZone,
  mountedAt,
}: {
  code: string
  announcement: AnnouncementView
  timeZone: string
  mountedAt: number
}) {
  const [editOpen, setEditOpen] = useState(false)
  const [deleteError, setDeleteError] = useState<string | null>(null)
  const updateMutation = useUpdateModuleAnnouncement(code)
  const deleteMutation = useDeleteModuleAnnouncement(code)
  const headingId = `module-announcement-${announcement.id}`
  const future = Date.parse(announcement.publishedAt) > mountedAt
  const expired = announcement.expiresAt !== null && Date.parse(announcement.expiresAt) <= mountedAt

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
      <article aria-labelledby={headingId} className="flex flex-col gap-3">
        {(announcement.pinned || future || expired) && (
          <div className="flex flex-wrap items-center gap-2">
            {announcement.pinned && (
              <Badge variant="warning" icon={Pin}>
                Pinned
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
        )}
        <h3 id={headingId} className="text-lg leading-snug font-semibold text-text">
          {announcement.title}
        </h3>
        <p className="text-sm leading-relaxed whitespace-pre-line text-text">{announcement.body}</p>
        <dl className="flex flex-wrap gap-x-6 gap-y-1 text-sm text-muted">
          <div className="flex gap-1.5">
            <dt>By</dt>
            <dd className="text-text">{announcement.author}</dd>
          </div>
          <div className="flex gap-1.5">
            <dt>{future ? 'Publishes' : 'Published'}</dt>
            <dd className="text-text">{formatDateTime(announcement.publishedAt, timeZone)}</dd>
          </div>
          {announcement.expiresAt && (
            <div className="flex gap-1.5">
              <dt>{expired ? 'Expired' : 'Expires'}</dt>
              <dd className="text-text">{formatDateTime(announcement.expiresAt, timeZone)}</dd>
            </div>
          )}
        </dl>
        <div className="flex flex-wrap items-center gap-2">
          <Dialog open={editOpen} onOpenChange={setEditOpen}>
            <DialogTrigger asChild>
              <Button variant="secondary" size="sm" aria-label={`Edit ${announcement.title}`}>
                <Pencil aria-hidden="true" className="size-4" />
                Edit
              </Button>
            </DialogTrigger>
            <DialogContent title="Edit announcement" size="wide">
              <AnnouncementForm
                announcement={announcement}
                timeZone={timeZone}
                onCancel={() => setEditOpen(false)}
                onSaved={() => {
                  setEditOpen(false)
                  toast.success('Announcement updated.')
                }}
                onSubmit={(request) =>
                  updateMutation.mutateAsync({ id: announcement.id, body: request })
                }
              />
            </DialogContent>
          </Dialog>
          <AlertDialog>
            <AlertDialogTrigger asChild>
              <Button
                variant="ghost"
                size="sm"
                className="text-danger"
                aria-label={`Delete ${announcement.title}`}
              >
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
      </article>
    </Card>
  )
}

/** `/lecturer/modules/:code/announcements` (05-frontend.md section 10): create, edit, delete. */
export function Component() {
  const { module, timeZone: contextZone } = useModuleContext()
  const timeZone = contextZone ?? DEFAULT_TIME_ZONE
  useDocumentTitle(`${module.code} announcements · RushDay`)
  const query = useModuleAnnouncements(module.code)
  const createMutation = useCreateModuleAnnouncement(module.code)
  // Radix unmounts DialogContent on close by default, so a fresh AnnouncementForm (and fresh
  // defaultValues) mounts every time the dialog opens; no key trick is needed.
  const [createOpen, setCreateOpen] = useState(false)
  // "Not published yet" and "Expired" are judged against when the list was opened.
  const [mountedAt] = useState(() => Date.now())

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
          <AnnouncementCard
            key={announcement.id}
            code={module.code}
            announcement={announcement}
            timeZone={timeZone}
            mountedAt={mountedAt}
          />
        ))}
      </div>
    )
  }

  return (
    <div className="flex flex-col gap-4">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <p className="text-sm text-muted">
          Students enrolled on {module.code} this year see these on their dashboard.
        </p>
        <Dialog open={createOpen} onOpenChange={setCreateOpen}>
          <DialogTrigger asChild>
            <Button>
              <Plus aria-hidden="true" className="size-4" />
              New announcement
            </Button>
          </DialogTrigger>
          <DialogContent title="New announcement" size="wide">
            <AnnouncementForm
              timeZone={timeZone}
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
