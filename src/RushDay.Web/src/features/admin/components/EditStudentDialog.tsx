import { useState } from 'react'
import { zodResolver } from '@hookform/resolvers/zod'
import { useForm } from 'react-hook-form'

import { describeProblem, isProblem, mapFieldErrors } from '@/api/problem'
import type { AdminStudentRecord } from '@/api/types/admin'
import {
  Button,
  Dialog,
  DialogClose,
  DialogContent,
  DialogFooter,
  FormError,
} from '@/components/ui'
import { toast } from '@/lib/toast'

import { useUpdateStudent } from '../hooks/useStudents'
import { orNull, studentSchema, type StudentFormValues } from '../lib/schemas'

import { StudentFields } from './StudentFields'

const FIELDS = ['fullName', 'programme', 'yearOfStudy', 'email'] as const

function EditStudentForm({ student, onDone }: { student: AdminStudentRecord; onDone: () => void }) {
  const update = useUpdateStudent(student.studentNumber)
  const [formError, setFormError] = useState<string | null>(null)
  const {
    register,
    handleSubmit,
    setError,
    formState: { errors, isSubmitting },
  } = useForm<StudentFormValues>({
    resolver: zodResolver(studentSchema),
    mode: 'onBlur',
    reValidateMode: 'onChange',
    defaultValues: {
      fullName: student.fullName,
      programme: student.programme,
      yearOfStudy: String(student.yearOfStudy),
      email: student.email ?? '',
    },
  })

  const submit = handleSubmit(async (values) => {
    setFormError(null)
    try {
      await update.mutateAsync({
        fullName: values.fullName,
        programme: values.programme,
        yearOfStudy: Number(values.yearOfStudy),
        email: orNull(values.email),
      })
      toast.success(`Saved ${student.studentNumber}.`)
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
      <StudentFields register={register} errors={errors} />
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

/** Edit a student's name, programme, year and email; the linked account's name follows. */
export function EditStudentDialog({
  student,
  open,
  onOpenChange,
}: {
  student: AdminStudentRecord
  open: boolean
  onOpenChange: (open: boolean) => void
}) {
  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent
        title={`Edit ${student.studentNumber}`}
        description="The linked account's display name changes with the record."
      >
        <EditStudentForm student={student} onDone={() => onOpenChange(false)} />
      </DialogContent>
    </Dialog>
  )
}
