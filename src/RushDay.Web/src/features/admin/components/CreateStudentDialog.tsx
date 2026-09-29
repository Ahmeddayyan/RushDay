import { useState } from 'react'
import { zodResolver } from '@hookform/resolvers/zod'
import { useForm } from 'react-hook-form'
import type { z } from 'zod'

import { describeProblem, isProblem, mapFieldErrors } from '@/api/problem'
import type { AdminStudentRow } from '@/api/types/admin'
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
} from '@/components/ui'
import { toast } from '@/lib/toast'

import { useProvisionAccount } from '../hooks/useAccounts'
import { useCreateStudent } from '../hooks/useStudents'
import { createStudentSchema, orNull } from '../lib/schemas'

import { StudentFields } from './StudentFields'
import { TemporaryPasswordDialog } from './TemporaryPasswordDialog'

type Input_ = z.input<typeof createStudentSchema>
type Output = z.output<typeof createStudentSchema>

const FIELDS = ['studentNumber', 'fullName', 'programme', 'yearOfStudy', 'email'] as const

export interface CreateStudentDialogProps {
  open: boolean
  onOpenChange: (open: boolean) => void
  /**
   * Offer "Provision an account now" (on by default), which chains to `POST /api/admin/accounts`
   * and shows the temporary password. The provisioning dialog turns it off: it provisions itself.
   */
  offerProvision?: boolean
  onCreated?: (student: AdminStudentRow) => void
}

interface Credentials {
  username: string
  password: string
  isDemo: boolean
}

function CreateStudentForm({
  offerProvision,
  onCreated,
  onProvisioned,
}: {
  offerProvision: boolean
  onCreated: (student: AdminStudentRow) => void
  onProvisioned: (credentials: Credentials) => void
}) {
  const createStudent = useCreateStudent()
  const provision = useProvisionAccount()
  const [formError, setFormError] = useState<string | null>(null)
  const {
    register,
    handleSubmit,
    setError,
    formState: { errors, isSubmitting },
  } = useForm<Input_, unknown, Output>({
    resolver: zodResolver(createStudentSchema),
    mode: 'onBlur',
    reValidateMode: 'onChange',
    defaultValues: {
      studentNumber: '',
      fullName: '',
      programme: '',
      yearOfStudy: '1',
      email: '',
      provision: offerProvision,
    },
  })

  const submit = handleSubmit(async (values) => {
    setFormError(null)
    let student: AdminStudentRow
    try {
      student = await createStudent.mutateAsync({
        studentNumber: values.studentNumber,
        fullName: values.fullName,
        programme: values.programme,
        yearOfStudy: Number(values.yearOfStudy),
        email: orNull(values.email),
      })
    } catch (error) {
      if (isProblem(error, 'student-number-taken')) {
        setError(
          'studentNumber',
          { message: describeProblem(error, { value: values.studentNumber }).message },
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
      return
    }

    toast.success(`Created ${student.studentNumber} ${student.fullName}.`)
    if (offerProvision && values.provision) {
      try {
        const result = await provision.mutateAsync({
          username: student.studentNumber,
          displayName: student.fullName,
          role: 'Student',
          studentNumber: student.studentNumber,
          email: student.email,
        })
        onProvisioned({
          username: result.account.username,
          password: result.temporaryPassword,
          isDemo: result.account.isDemo,
        })
      } catch (error) {
        toast.error(
          `${student.studentNumber} was created, but the account wasn't: ${
            describeProblem(error, { number: student.studentNumber }).message
          } Use Provision account on the Accounts page.`,
        )
      }
    }
    onCreated(student)
  })

  return (
    <form onSubmit={(event) => void submit(event)} noValidate className="flex flex-col gap-4">
      <FormField
        label="Student number"
        required
        error={errors.studentNumber?.message}
        hint="S followed by six digits."
      >
        <Input
          {...register('studentNumber')}
          autoComplete="off"
          maxLength={7}
          placeholder="S000123"
          className="font-mono"
        />
      </FormField>
      <StudentFields register={register} errors={errors} />
      {offerProvision && (
        <Checkbox
          {...register('provision')}
          label="Provision an account now"
          hint="Creates a sign-in with a temporary password, shown once."
        />
      )}
      <FormError>{formError}</FormError>
      <DialogFooter>
        <DialogClose asChild>
          <Button variant="secondary">Cancel</Button>
        </DialogClose>
        <Button type="submit" loading={isSubmitting}>
          Create student
        </Button>
      </DialogFooter>
    </form>
  )
}

/** "Create student" (05-frontend.md section 10, `/admin/students`). */
export function CreateStudentDialog({
  open,
  onOpenChange,
  offerProvision = true,
  onCreated,
}: CreateStudentDialogProps) {
  const [credentials, setCredentials] = useState<Credentials | null>(null)

  return (
    <>
      <Dialog open={open} onOpenChange={onOpenChange}>
        <DialogContent
          title="Create student"
          description="Adds a student record. Enrolments and marks follow from it."
        >
          <CreateStudentForm
            offerProvision={offerProvision}
            onProvisioned={setCredentials}
            onCreated={(student) => {
              onOpenChange(false)
              onCreated?.(student)
            }}
          />
        </DialogContent>
      </Dialog>
      <TemporaryPasswordDialog credentials={credentials} onClose={() => setCredentials(null)} />
    </>
  )
}
