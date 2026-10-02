import { useState } from 'react'
import { zodResolver } from '@hookform/resolvers/zod'
import { useForm } from 'react-hook-form'

import { describeProblem, isProblem, mapFieldErrors } from '@/api/problem'
import type { AdminLecturer } from '@/api/types/admin'
import {
  Button,
  Dialog,
  DialogClose,
  DialogContent,
  DialogFooter,
  FormError,
} from '@/components/ui'
import { toast } from '@/lib/toast'

import { useUpdateLecturer } from '../hooks/useLecturers'
import { LECTURER_TITLES, lecturerSchema, orNull, type LecturerFormValues } from '../lib/schemas'

import { LecturerFields } from './LecturerFields'

const FIELDS = ['fullName', 'title', 'department', 'email'] as const

function knownTitle(title: string): LecturerFormValues['title'] {
  return (LECTURER_TITLES as readonly string[]).includes(title)
    ? (title as LecturerFormValues['title'])
    : 'Dr'
}

function EditLecturerForm({ lecturer, onDone }: { lecturer: AdminLecturer; onDone: () => void }) {
  const update = useUpdateLecturer()
  const [formError, setFormError] = useState<string | null>(null)
  const {
    register,
    handleSubmit,
    setError,
    formState: { errors, isSubmitting },
  } = useForm<LecturerFormValues>({
    resolver: zodResolver(lecturerSchema),
    mode: 'onBlur',
    reValidateMode: 'onChange',
    defaultValues: {
      title: knownTitle(lecturer.title),
      fullName: lecturer.fullName,
      department: lecturer.department,
      email: lecturer.email ?? '',
    },
  })

  const submit = handleSubmit(async (values) => {
    setFormError(null)
    try {
      await update.mutateAsync({
        staffNumber: lecturer.staffNumber,
        body: {
          fullName: values.fullName,
          title: values.title,
          department: values.department.toUpperCase(),
          email: orNull(values.email),
        },
      })
      toast.success(`Saved ${lecturer.staffNumber}.`)
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
      <LecturerFields register={register} errors={errors} />
      <FormError>{formError}</FormError>
      <DialogFooter>
        <DialogClose asChild>
          <Button variant="secondary">Cancel</Button>
        </DialogClose>
        <Button type="submit" loading={isSubmitting}>
          Save changes
        </Button>
      </DialogFooter>
    </form>
  )
}

export function EditLecturerDialog({
  lecturer,
  onOpenChange,
}: {
  /** Null closes the dialog. */
  lecturer: AdminLecturer | null
  onOpenChange: (open: boolean) => void
}) {
  return (
    <Dialog open={lecturer !== null} onOpenChange={onOpenChange}>
      {lecturer && (
        <DialogContent
          title={`Edit ${lecturer.staffNumber}`}
          description="The linked account's display name changes with the record."
        >
          <EditLecturerForm lecturer={lecturer} onDone={() => onOpenChange(false)} />
        </DialogContent>
      )}
    </Dialog>
  )
}
