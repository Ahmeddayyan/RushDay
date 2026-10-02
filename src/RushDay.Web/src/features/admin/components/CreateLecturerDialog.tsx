import { useState } from 'react'
import { zodResolver } from '@hookform/resolvers/zod'
import { useForm } from 'react-hook-form'
import type { z } from 'zod'

import { describeProblem, isProblem, mapFieldErrors } from '@/api/problem'
import {
  Button,
  Dialog,
  DialogClose,
  DialogContent,
  DialogFooter,
  FormError,
  FormField,
  Input,
} from '@/components/ui'
import { toast } from '@/lib/toast'

import { useCreateLecturer } from '../hooks/useLecturers'
import { createLecturerSchema, orNull } from '../lib/schemas'

import { LecturerFields } from './LecturerFields'

type FormInput = z.input<typeof createLecturerSchema>
type FormOutput = z.output<typeof createLecturerSchema>

const FIELDS = ['staffNumber', 'fullName', 'title', 'department', 'email'] as const

function CreateLecturerForm({ onDone }: { onDone: () => void }) {
  const create = useCreateLecturer()
  const [formError, setFormError] = useState<string | null>(null)
  const {
    register,
    handleSubmit,
    setError,
    formState: { errors, isSubmitting },
  } = useForm<FormInput, unknown, FormOutput>({
    resolver: zodResolver(createLecturerSchema),
    mode: 'onBlur',
    reValidateMode: 'onChange',
    defaultValues: { staffNumber: '', title: 'Dr', fullName: '', department: '', email: '' },
  })

  const submit = handleSubmit(async (values) => {
    setFormError(null)
    try {
      const lecturer = await create.mutateAsync({
        staffNumber: values.staffNumber,
        fullName: values.fullName,
        title: values.title,
        department: values.department.toUpperCase(),
        email: orNull(values.email),
      })
      toast.success(`Created ${lecturer.staffNumber} ${lecturer.title} ${lecturer.fullName}.`)
      onDone()
    } catch (error) {
      if (isProblem(error, 'staff-number-taken')) {
        setError(
          'staffNumber',
          { message: describeProblem(error, { value: values.staffNumber }).message },
          { shouldFocus: true },
        )
      } else if (isProblem(error, 'validation')) {
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
        label="Staff number"
        required
        error={errors.staffNumber?.message}
        hint="L followed by five digits."
      >
        <Input
          {...register('staffNumber')}
          autoComplete="off"
          maxLength={6}
          placeholder="L00041"
          className="font-mono"
        />
      </FormField>
      <LecturerFields register={register} errors={errors} />
      <FormError>{formError}</FormError>
      <DialogFooter>
        <DialogClose asChild>
          <Button variant="secondary">Cancel</Button>
        </DialogClose>
        <Button type="submit" loading={isSubmitting}>
          Create lecturer
        </Button>
      </DialogFooter>
    </form>
  )
}

/** "Create lecturer" (05-frontend.md section 10, `/admin/lecturers`). */
export function CreateLecturerDialog({
  open,
  onOpenChange,
}: {
  open: boolean
  onOpenChange: (open: boolean) => void
}) {
  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent
        title="Create lecturer"
        description="Adds a lecturer record. Provision an account separately on the Accounts page."
      >
        <CreateLecturerForm onDone={() => onOpenChange(false)} />
      </DialogContent>
    </Dialog>
  )
}
