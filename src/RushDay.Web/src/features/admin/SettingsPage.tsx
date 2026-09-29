import { useState } from 'react'
import { zodResolver } from '@hookform/resolvers/zod'
import { TriangleAlert } from 'lucide-react'
import { useForm, useWatch } from 'react-hook-form'

import { describeProblem, isProblem, mapFieldErrors } from '@/api/problem'
import type { AdminSettings, UpdateSettingsRequest } from '@/api/types/admin'
import {
  AlertDialog,
  AlertDialogCancel,
  AlertDialogContent,
  AlertDialogFooter,
  Button,
  Card,
  ErrorState,
  FormError,
  FormField,
  Input,
  LoadingRegion,
  PageHeader,
  Select,
  Skeleton,
} from '@/components/ui'
import { formatDateTime } from '@/lib/format'
import { toast } from '@/lib/toast'
import { useDirtyForm } from '@/lib/useDirtyForm'
import { useDocumentTitle } from '@/lib/useDocumentTitle'

import { useAdminSettings, useUpdateSettings } from './hooks/useSettings'
import { orNull, settingsSchema, type SettingsFormValues } from './lib/schemas'

const FIELDS = [
  'academicYear',
  'currentSemester',
  'institutionName',
  'institutionShortName',
  'timeZone',
  'supportEmail',
  'supportUrl',
] as const

function yearWarning(newYear: string): string {
  return `Changing the year starts a new year for everyone: this year's enrolment windows no longer apply, credit budgets and module places start from zero, and last year's modules become 'completed'. Create the windows for ${newYear} first.`
}

function toRequest(values: SettingsFormValues): UpdateSettingsRequest {
  return {
    academicYear: values.academicYear,
    currentSemester: values.currentSemester,
    institutionName: values.institutionName,
    institutionShortName: values.institutionShortName,
    timeZone: values.timeZone,
    supportEmail: orNull(values.supportEmail),
    supportUrl: orNull(values.supportUrl),
  }
}

function SettingsForm({ settings }: { settings: AdminSettings }) {
  const update = useUpdateSettings()
  const [formError, setFormError] = useState<string | null>(null)
  const [pendingYearChange, setPendingYearChange] = useState<SettingsFormValues | null>(null)
  const {
    register,
    handleSubmit,
    setError,
    control,
    reset,
    formState: { errors, isSubmitting, isDirty },
  } = useForm<SettingsFormValues>({
    resolver: zodResolver(settingsSchema),
    mode: 'onBlur',
    reValidateMode: 'onChange',
    defaultValues: {
      academicYear: settings.academicYear,
      currentSemester: settings.currentSemester,
      institutionName: settings.institutionName,
      institutionShortName: settings.institutionShortName,
      timeZone: settings.timeZone,
      supportEmail: settings.supportEmail ?? '',
      supportUrl: settings.supportUrl ?? '',
    },
  })
  useDirtyForm(isDirty)
  const academicYear = useWatch({ control, name: 'academicYear' })
  const yearChanged = academicYear.trim() !== settings.academicYear

  async function save(values: SettingsFormValues) {
    setFormError(null)
    try {
      const saved = await update.mutateAsync(toRequest(values))
      reset({
        academicYear: saved.academicYear,
        currentSemester: saved.currentSemester,
        institutionName: saved.institutionName,
        institutionShortName: saved.institutionShortName,
        timeZone: saved.timeZone,
        supportEmail: saved.supportEmail ?? '',
        supportUrl: saved.supportUrl ?? '',
      })
      toast.success(
        saved.academicYear !== settings.academicYear
          ? `Settings saved. The academic year is now ${saved.academicYear}.`
          : 'Settings saved.',
      )
      return true
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
      return false
    }
  }

  const submit = handleSubmit(async (values) => {
    if (values.academicYear !== settings.academicYear) {
      setPendingYearChange(values)
      return
    }
    await save(values)
  })

  return (
    <>
      <form onSubmit={(event) => void submit(event)} noValidate className="flex flex-col gap-5">
        <FormField
          label="Academic year"
          required
          error={errors.academicYear?.message}
          hint={
            yearChanged ? (
              <span className="flex items-start gap-1.5 font-medium text-warning">
                <TriangleAlert aria-hidden="true" className="mt-0.5 size-4 shrink-0" />
                <span>{yearWarning(academicYear.trim())}</span>
              </span>
            ) : (
              'Written like 2026/27.'
            )
          }
        >
          <Input {...register('academicYear')} maxLength={7} className="max-w-32" />
        </FormField>
        <FormField
          label="Current semester"
          required
          error={errors.currentSemester?.message}
          hint="Decides whose classes appear on this week's timetables."
        >
          <Select
            {...register('currentSemester')}
            options={[
              { value: 'autumn', label: 'Autumn' },
              { value: 'spring', label: 'Spring' },
            ]}
            className="max-w-48"
          />
        </FormField>
        <div className="grid gap-5 sm:grid-cols-[2fr_1fr]">
          <FormField label="Institution name" required error={errors.institutionName?.message}>
            <Input {...register('institutionName')} maxLength={200} />
          </FormField>
          <FormField label="Short name" required error={errors.institutionShortName?.message}>
            <Input {...register('institutionShortName')} maxLength={32} />
          </FormField>
        </div>
        <FormField
          label="Time zone"
          required
          error={errors.timeZone?.message}
          hint="An IANA time zone id, for example Europe/London. Every date and time is shown in it."
        >
          <Input
            {...register('timeZone')}
            maxLength={64}
            spellCheck={false}
            className="max-w-72 font-mono"
          />
        </FormField>
        <div className="grid gap-5 sm:grid-cols-2">
          <FormField
            label="Academic office email"
            error={errors.supportEmail?.message}
            hint="Shown wherever students are told to contact the academic office."
          >
            <Input {...register('supportEmail')} type="email" maxLength={256} />
          </FormField>
          <FormField
            label="Academic office help URL"
            error={errors.supportUrl?.message}
            hint="Shown wherever students are told to contact the academic office."
          >
            <Input {...register('supportUrl')} type="url" maxLength={400} placeholder="https://" />
          </FormField>
        </div>
        <FormError>{formError}</FormError>
        <div className="flex flex-wrap items-center gap-3">
          <Button type="submit" loading={isSubmitting || update.isPending} disabled={!isDirty}>
            Save settings
          </Button>
          <span className="text-sm text-muted">
            Last changed {formatDateTime(settings.updatedAt, settings.timeZone)}
          </span>
        </div>
      </form>

      <AlertDialog
        open={pendingYearChange !== null}
        onOpenChange={(open) => !open && setPendingYearChange(null)}
      >
        <AlertDialogContent
          title={`Change the academic year to ${pendingYearChange?.academicYear ?? ''}?`}
          description={yearWarning(pendingYearChange?.academicYear ?? '')}
        >
          <AlertDialogFooter>
            <AlertDialogCancel>Cancel</AlertDialogCancel>
            <Button
              variant="danger"
              loading={update.isPending}
              onClick={() => {
                if (!pendingYearChange) return
                void save(pendingYearChange).then((ok) => {
                  if (ok) setPendingYearChange(null)
                })
              }}
            >
              Start {pendingYearChange?.academicYear ?? 'the new year'}
            </Button>
          </AlertDialogFooter>
        </AlertDialogContent>
      </AlertDialog>
    </>
  )
}

/** `/admin/settings` (05-frontend.md section 10). */
export function Component() {
  useDocumentTitle('Settings · RushDay')
  const query = useAdminSettings()

  let content
  if (query.isPending) {
    content = (
      <LoadingRegion label="settings">
        <div className="flex flex-col gap-5">
          {[0, 1, 2, 3, 4].map((key) => (
            <Skeleton key={key} className="h-16 w-full" />
          ))}
        </div>
      </LoadingRegion>
    )
  } else if (query.isError) {
    content = <ErrorState error={query.error} onRetry={() => void query.refetch()} />
  } else {
    content = <SettingsForm key={query.data.updatedAt} settings={query.data} />
  }

  return (
    <div className="max-w-3xl">
      <PageHeader
        title="Settings"
        description="The academic year, the semester being taught, and how the institution appears."
      />
      <Card>{content}</Card>
    </div>
  )
}
