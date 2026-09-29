import { useState } from 'react'
import { zodResolver } from '@hookform/resolvers/zod'
import { useForm, useWatch } from 'react-hook-form'
import { z } from '@/lib/zod'

import { describeProblem, isProblem, mapFieldErrors } from '@/api/problem'
import type { AnnouncementRequest } from '@/api/types/admin'
import type { AnnouncementView } from '@/api/types/common'
import {
  Button,
  Checkbox,
  Dialog,
  DialogClose,
  DialogContent,
  DialogFooter,
  FormError,
  FormField,
  Input,
  Textarea,
} from '@/components/ui'
import { formatNumber } from '@/lib/format'
import { toast } from '@/lib/toast'
import { useDirtyForm } from '@/lib/useDirtyForm'

import { useSaveAnnouncement } from '../hooks/useAdminAnnouncements'
import { ANNOUNCEMENT_BODY_MAX, ANNOUNCEMENT_TITLE_MAX } from '../lib/schemas'
import { toApiInstant, toZonedInput, zonedInputToMs } from '@/lib/zonedTime'

import { ZonedDateTimeField } from '@/components/ui/ZonedDateTimeField'

function announcementSchema(timeZone: string) {
  return z
    .object({
      title: z
        .string()
        .trim()
        .min(1, 'Enter a title.')
        .max(ANNOUNCEMENT_TITLE_MAX, `Use ${ANNOUNCEMENT_TITLE_MAX} characters or fewer.`),
      body: z
        .string()
        .trim()
        .min(1, 'Write the announcement.')
        .max(
          ANNOUNCEMENT_BODY_MAX,
          `Use ${formatNumber(ANNOUNCEMENT_BODY_MAX)} characters or fewer.`,
        ),
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

type FormValues = z.infer<ReturnType<typeof announcementSchema>>

const FIELDS = ['title', 'body', 'pinned', 'publishedAt', 'expiresAt'] as const

function AnnouncementForm({
  announcement,
  timeZone,
  onDone,
}: {
  announcement: AnnouncementView | null
  timeZone: string
  onDone: () => void
}) {
  const save = useSaveAnnouncement()
  const [formError, setFormError] = useState<string | null>(null)
  const {
    register,
    handleSubmit,
    setError,
    control,
    formState: { errors, isSubmitting, isDirty },
  } = useForm<FormValues>({
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
  useDirtyForm(isDirty)
  const body = useWatch({ control, name: 'body' })
  const publishedAt = useWatch({ control, name: 'publishedAt' })
  const expiresAt = useWatch({ control, name: 'expiresAt' })

  const submit = handleSubmit(async (values) => {
    setFormError(null)
    const published = values.publishedAt ? zonedInputToMs(values.publishedAt, timeZone) : null
    const expires = values.expiresAt ? zonedInputToMs(values.expiresAt, timeZone) : null
    const request: AnnouncementRequest = {
      title: values.title,
      body: values.body,
      pinned: values.pinned,
      ...(published !== null ? { publishedAt: toApiInstant(published) } : {}),
      ...(expires !== null ? { expiresAt: toApiInstant(expires) } : {}),
    }
    try {
      await save.mutateAsync({ id: announcement?.id ?? null, body: request })
      toast.success(announcement ? 'Announcement saved.' : 'Announcement posted.')
      onDone()
    } catch (error) {
      if (isProblem(error, 'validation')) {
        const { fields, other } = mapFieldErrors(error, FIELDS)
        for (const field of FIELDS) {
          const message = fields[field]
          if (message) setError(field, { message })
        }
        setFormError(other.join(' ') || null)
      } else {
        setFormError(describeProblem(error).message)
      }
    }
  })

  return (
    <form onSubmit={(event) => void submit(event)} noValidate className="flex flex-col gap-4">
      <FormField
        label="Title"
        required
        error={errors.title?.message}
        hint={`At most ${ANNOUNCEMENT_TITLE_MAX} characters.`}
      >
        <Input {...register('title')} maxLength={ANNOUNCEMENT_TITLE_MAX} autoComplete="off" />
      </FormField>
      <FormField
        label="Message"
        required
        error={errors.body?.message}
        hint={
          <span aria-live="polite">
            {formatNumber(body.length)} of {formatNumber(ANNOUNCEMENT_BODY_MAX)} characters. Plain
            text; line breaks are kept.
          </span>
        }
      >
        <Textarea {...register('body')} rows={6} maxLength={ANNOUNCEMENT_BODY_MAX} />
      </FormField>
      <Checkbox {...register('pinned')} label="Pin to the top" />
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
        <DialogClose asChild>
          <Button variant="secondary">Cancel</Button>
        </DialogClose>
        <Button type="submit" loading={isSubmitting}>
          {announcement ? 'Save announcement' : 'Post announcement'}
        </Button>
      </DialogFooter>
    </form>
  )
}

/**
 * `AnnouncementDialog` for the registry: a new university announcement, or an edit of any
 * announcement (administrators may edit any scope, 02-api.md section 8.5).
 */
export function AnnouncementDialog({
  open,
  onOpenChange,
  announcement,
  timeZone,
}: {
  open: boolean
  onOpenChange: (open: boolean) => void
  /** Null posts a new university announcement. */
  announcement: AnnouncementView | null
  timeZone: string
}) {
  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent
        size="wide"
        title={announcement ? 'Edit announcement' : 'New university announcement'}
        description={
          announcement?.scope === 'module'
            ? `A module announcement for ${announcement.moduleCode ?? 'a module'}.`
            : 'Every student and lecturer sees it once it is published.'
        }
      >
        <AnnouncementForm
          announcement={announcement}
          timeZone={timeZone}
          onDone={() => onOpenChange(false)}
        />
      </DialogContent>
    </Dialog>
  )
}
