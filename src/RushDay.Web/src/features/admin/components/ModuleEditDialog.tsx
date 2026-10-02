import { useState } from 'react'
import { zodResolver } from '@hookform/resolvers/zod'
import { TriangleAlert } from 'lucide-react'
import { useForm, useWatch, type Resolver } from 'react-hook-form'

import { describeProblem, isProblem, mapFieldErrors } from '@/api/problem'
import type { AdminModule } from '@/api/types/admin'
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
  Select,
  Textarea,
} from '@/components/ui'
import { toast } from '@/lib/toast'

import { useCreateModule, useUpdateModule } from '../hooks/useModules'
import {
  createModuleSchema,
  editModuleSchema,
  orNull,
  type CreateModuleFormValues,
} from '../lib/schemas'

const FIELDS = [
  'code',
  'title',
  'description',
  'credits',
  'capacity',
  'semester',
  'isActive',
] as const

const SEMESTERS = [
  { value: 'autumn', label: 'Autumn' },
  { value: 'spring', label: 'Spring' },
]

function Warning({ children }: { children: string }) {
  return (
    <span className="flex items-start gap-1.5 font-medium text-warning">
      <TriangleAlert aria-hidden="true" className="mt-0.5 size-4 shrink-0" />
      <span>{children}</span>
    </span>
  )
}

function ModuleForm({ module, onDone }: { module: AdminModule | null; onDone: () => void }) {
  const editing = module !== null
  const enrolled = module?.enrolledCount ?? 0
  const create = useCreateModule()
  const update = useUpdateModule()
  const [formError, setFormError] = useState<string | null>(null)
  const {
    register,
    handleSubmit,
    setError,
    control,
    formState: { errors, isSubmitting },
  } = useForm<CreateModuleFormValues>({
    resolver: zodResolver(
      editing ? editModuleSchema : createModuleSchema,
    ) as Resolver<CreateModuleFormValues>,
    mode: 'onBlur',
    reValidateMode: 'onChange',
    defaultValues: {
      code: module?.code ?? '',
      title: module?.title ?? '',
      description: module?.description ?? '',
      credits: String(module?.credits ?? 15),
      capacity: String(module?.capacity ?? 30),
      semester: module?.semester ?? 'autumn',
      isActive: module?.isActive ?? true,
    },
  })

  const credits = useWatch({ control, name: 'credits' })
  const capacity = useWatch({ control, name: 'capacity' })
  const creditsChanged = editing && enrolled > 0 && Number(credits) !== module.credits
  const capacityBelow =
    editing && Number(capacity) !== module.capacity && Number(capacity) < enrolled
  const semesterLocked = editing && enrolled > 0

  const submit = handleSubmit(async (values) => {
    setFormError(null)
    const body = {
      title: values.title.trim(),
      description: orNull(values.description),
      credits: Number(values.credits),
      capacity: Number(values.capacity),
      semester: values.semester,
    }
    try {
      if (editing) {
        await update.mutateAsync({
          code: module.code,
          body: { ...body, isActive: values.isActive },
        })
        toast.success(`Saved ${module.code}.`)
      } else {
        const code = values.code.trim().toUpperCase()
        await create.mutateAsync({ ...body, code })
        toast.success(`Created ${code}.`)
      }
      onDone()
    } catch (error) {
      if (isProblem(error, 'module-code-taken')) {
        setError(
          'code',
          { message: describeProblem(error, { value: values.code.trim().toUpperCase() }).message },
          { shouldFocus: true },
        )
      } else if (isProblem(error, 'capacity-below-enrolled')) {
        setError('capacity', { message: describeProblem(error).message }, { shouldFocus: true })
      } else if (isProblem(error, 'semester-change-with-enrolments')) {
        setError('semester', { message: describeProblem(error).message })
      } else if (isProblem(error, 'validation')) {
        const { fields, other } = mapFieldErrors(error, FIELDS)
        for (const field of FIELDS) {
          const message = fields[field]
          if (message) setError(field, { message })
        }
        setFormError(other.join(' ') || null)
      } else {
        setFormError(describeProblem(error, editing ? { code: module.code } : {}).message)
      }
    }
  })

  return (
    <form onSubmit={(event) => void submit(event)} noValidate className="flex flex-col gap-4">
      {!editing && (
        <FormField
          label="Module code"
          required
          error={errors.code?.message}
          hint="Two letters and four digits; the first digit is the level."
        >
          <Input
            {...register('code')}
            autoComplete="off"
            maxLength={6}
            placeholder="CS3099"
            className="max-w-40 font-mono uppercase"
          />
        </FormField>
      )}
      <FormField label="Title" required error={errors.title?.message}>
        <Input {...register('title')} autoComplete="off" maxLength={200} />
      </FormField>
      <FormField label="Description" error={errors.description?.message} hint="Optional.">
        <Textarea {...register('description')} rows={3} maxLength={2000} />
      </FormField>
      <div className="grid gap-4 sm:grid-cols-2">
        <FormField
          label="Credits"
          required
          error={errors.credits?.message}
          hint={
            creditsChanged ? (
              <Warning>{`Changing credits affects ${enrolled} enrolled students' budgets`}</Warning>
            ) : (
              '5 to 60.'
            )
          }
        >
          <Input
            {...register('credits')}
            type="text"
            inputMode="numeric"
            pattern="[0-9]*"
            maxLength={2}
          />
        </FormField>
        <FormField
          label="Capacity"
          required
          error={errors.capacity?.message}
          hint={
            capacityBelow ? (
              <Warning>{`Below current enrolment (${enrolled})`}</Warning>
            ) : editing ? (
              `${enrolled} enrolled this year.`
            ) : (
              '0 to 10,000 places.'
            )
          }
        >
          <Input
            {...register('capacity')}
            type="text"
            inputMode="numeric"
            pattern="[0-9]*"
            maxLength={5}
          />
        </FormField>
      </div>
      <FormField
        label="Semester"
        required
        error={errors.semester?.message}
        hint={
          // The server refuses a semester change once the module has enrolments or marks in any
          // year (S6 review), not only this year's: say so before the administrator tries.
          semesterLocked
            ? `Can't change: ${enrolled} students are enrolled this year. Create a new module instead.`
            : editing
              ? "Decides which timetable and credit budget the module counts towards. Can't change once any student has enrolled, in any year."
              : 'Decides which timetable and credit budget the module counts towards.'
        }
      >
        <Select {...register('semester')} options={SEMESTERS} disabled={semesterLocked} />
      </FormField>
      {editing && (
        <Checkbox
          {...register('isActive')}
          label="Running (active)"
          hint="An inactive module leaves the catalogue; students keep their history."
        />
      )}
      <FormError>{formError}</FormError>
      <DialogFooter>
        <DialogClose asChild>
          <Button variant="secondary">Cancel</Button>
        </DialogClose>
        <Button type="submit" loading={isSubmitting}>
          {editing ? 'Save changes' : 'Create module'}
        </Button>
      </DialogFooter>
    </form>
  )
}

/**
 * "New module" and "Edit" (05-frontend.md section 10, `/admin/modules`): the credits and capacity
 * warnings, and the semester locked once students have enrolled; the server enforces both rules.
 */
export function ModuleEditDialog({
  open,
  onOpenChange,
  module,
}: {
  open: boolean
  onOpenChange: (open: boolean) => void
  /** Null creates a new module. */
  module: AdminModule | null
}) {
  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent
        size="wide"
        title={module ? `Edit ${module.code}` : 'New module'}
        description={
          module ? module.title : 'Add a module to the catalogue. Assign lecturers afterwards.'
        }
      >
        <ModuleForm module={module} onDone={() => onOpenChange(false)} />
      </DialogContent>
    </Dialog>
  )
}
