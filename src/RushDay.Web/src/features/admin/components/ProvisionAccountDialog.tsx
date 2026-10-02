import { useState } from 'react'
import { zodResolver } from '@hookform/resolvers/zod'
import { UserPlus } from 'lucide-react'
import { useForm } from 'react-hook-form'

import { describeProblem, isProblem, mapFieldErrors } from '@/api/problem'
import type { AdminLecturer, AdminStudentRow, ProvisionAccountResponse } from '@/api/types/admin'
import type { Role } from '@/api/types/common'
import {
  Button,
  Combobox,
  Dialog,
  DialogClose,
  DialogContent,
  DialogFooter,
  FormError,
  FormField,
  Input,
  PasswordInput,
  Select,
} from '@/components/ui'
import { useDebouncedValue } from '@/lib/useDebouncedValue'

import { useProvisionAccount } from '../hooks/useAccounts'
import { useLecturers } from '../hooks/useLecturers'
import { useStudents } from '../hooks/useStudents'
import { ROLE_OPTIONS } from '../lib/accounts'
import { orNull, provisionSchema, type ProvisionFormValues } from '../lib/schemas'

import { CreateStudentDialog } from './CreateStudentDialog'
import { TemporaryPasswordDialog } from './TemporaryPasswordDialog'

/** The student an account is for: a row of `GET /api/admin/students`, or one just created. */
type StudentChoice = AdminStudentRow

const FIELDS = ['username', 'displayName', 'email', 'temporaryPassword'] as const

const NO_STUDENTS =
  'Every student already has an account. Create a student first, or use Reset password on an existing one.'
const NO_LECTURERS =
  'Every lecturer already has an account. Create a lecturer first, or use Reset password on an existing one.'

function ProvisionForm({
  initialStudent,
  onDone,
}: {
  initialStudent: StudentChoice | null
  onDone: (result: ProvisionAccountResponse) => void
}) {
  const provision = useProvisionAccount()
  const [role, setRole] = useState<Role>('Student')
  const [student, setStudent] = useState<StudentChoice | null>(initialStudent)
  const [studentText, setStudentText] = useState(
    initialStudent ? `${initialStudent.studentNumber} ${initialStudent.fullName}` : '',
  )
  const [lecturer, setLecturer] = useState<AdminLecturer | null>(null)
  const [lecturerText, setLecturerText] = useState('')
  const [principalError, setPrincipalError] = useState<string | null>(null)
  const [formError, setFormError] = useState<string | null>(null)
  const [createStudentOpen, setCreateStudentOpen] = useState(false)

  const studentQuery = useDebouncedValue(student ? '' : studentText, 250)
  const students = useStudents(
    { q: studentQuery, accountState: 'none', pageSize: 10 },
    { enabled: role === 'Student' },
  )
  const lecturerQuery = useDebouncedValue(lecturer ? '' : lecturerText, 250)
  const lecturers = useLecturers(lecturerQuery, { enabled: role === 'Lecturer' })
  const lecturerChoices = (lecturers.data ?? []).filter(
    (item) => !item.hasAccount && item.leftAt === null,
  )

  const {
    register,
    handleSubmit,
    setError,
    setValue,
    formState: { errors, isSubmitting },
  } = useForm<ProvisionFormValues>({
    resolver: zodResolver(provisionSchema),
    mode: 'onBlur',
    reValidateMode: 'onChange',
    defaultValues: {
      username: initialStudent?.studentNumber ?? '',
      displayName: initialStudent?.fullName ?? '',
      email: initialStudent?.email ?? '',
      temporaryPassword: '',
    },
  })

  function fill(username: string, displayName: string, email: string | null) {
    const options = { shouldDirty: true, shouldValidate: true }
    setValue('username', username, options)
    setValue('displayName', displayName, options)
    setValue('email', email ?? '', options)
  }

  function chooseStudent(choice: StudentChoice | null) {
    setStudent(choice)
    setPrincipalError(null)
    if (choice) fill(choice.studentNumber, choice.fullName, choice.email)
  }

  function chooseLecturer(choice: AdminLecturer | null) {
    setLecturer(choice)
    setPrincipalError(null)
    if (choice) fill(choice.staffNumber, `${choice.title} ${choice.fullName}`, choice.email)
  }

  function changeRole(next: Role) {
    setRole(next)
    setPrincipalError(null)
    setStudent(null)
    setStudentText('')
    setLecturer(null)
    setLecturerText('')
    fill('', '', null)
  }

  const submit = handleSubmit(async (values) => {
    setFormError(null)
    if (role === 'Student' && !student) {
      setPrincipalError('Choose the student this account is for.')
      return
    }
    if (role === 'Lecturer' && !lecturer) {
      setPrincipalError('Choose the lecturer this account is for.')
      return
    }
    const number = role === 'Student' ? student?.studentNumber : lecturer?.staffNumber
    try {
      const result = await provision.mutateAsync({
        username: values.username,
        displayName: values.displayName,
        role,
        studentNumber: role === 'Student' ? (student?.studentNumber ?? null) : null,
        staffNumber: role === 'Lecturer' ? (lecturer?.staffNumber ?? null) : null,
        email: orNull(values.email),
        ...(values.temporaryPassword ? { temporaryPassword: values.temporaryPassword } : {}),
      })
      onDone(result)
    } catch (error) {
      if (isProblem(error, 'username-taken')) {
        setError('username', { message: describeProblem(error).message }, { shouldFocus: true })
      } else if (isProblem(error, 'weak-password')) {
        setError('temporaryPassword', { message: describeProblem(error).message })
      } else if (isProblem(error, 'validation')) {
        const { fields, other } = mapFieldErrors(error, FIELDS)
        for (const field of FIELDS) {
          const message = fields[field]
          if (message) setError(field, { message })
        }
        setFormError(other.join(' ') || null)
      } else {
        setFormError(describeProblem(error, number ? { number } : {}).message)
      }
    }
  })

  return (
    <>
      <form onSubmit={(event) => void submit(event)} noValidate className="flex flex-col gap-4">
        <FormField
          label="Role"
          required
          hint="Choose the role first: it decides who the account is for."
        >
          <Select
            value={role}
            onChange={(event) => changeRole(event.target.value as Role)}
            options={ROLE_OPTIONS}
          />
        </FormField>

        {role === 'Student' && (
          <div className="flex flex-col gap-2">
            <FormField
              label="Student"
              required
              error={principalError ?? undefined}
              hint="Search by student number or name. Only students without an account are listed."
            >
              <Combobox
                items={students.data?.items ?? []}
                getKey={(item) => item.studentNumber}
                getLabel={(item) => `${item.studentNumber} ${item.fullName}`}
                renderItem={(item) => (
                  <span className="flex flex-col">
                    <span>
                      <span className="font-mono">{item.studentNumber}</span> {item.fullName}
                    </span>
                    <span className="text-xs text-muted">{item.programme}</span>
                  </span>
                )}
                value={student}
                onChange={chooseStudent}
                inputValue={studentText}
                onInputChange={setStudentText}
                loading={students.isFetching}
                placeholder="S000123 or a name"
                emptyState={
                  studentQuery.trim() === ''
                    ? NO_STUDENTS
                    : `No students without an account match "${studentQuery.trim()}".`
                }
              />
            </FormField>
            <div>
              <Button variant="ghost" size="sm" onClick={() => setCreateStudentOpen(true)}>
                <UserPlus aria-hidden="true" className="size-4" />
                Create student
              </Button>
            </div>
          </div>
        )}

        {role === 'Lecturer' && (
          <FormField
            label="Lecturer"
            required
            error={principalError ?? undefined}
            hint="Search by staff number or name. Only lecturers without an account are listed."
          >
            <Combobox
              items={lecturerChoices}
              getKey={(item) => item.staffNumber}
              getLabel={(item) => `${item.staffNumber} ${item.title} ${item.fullName}`}
              renderItem={(item) => (
                <span>
                  <span className="font-mono">{item.staffNumber}</span> {item.title} {item.fullName}
                </span>
              )}
              value={lecturer}
              onChange={chooseLecturer}
              inputValue={lecturerText}
              onInputChange={setLecturerText}
              loading={lecturers.isFetching}
              placeholder="L00001 or a name"
              emptyState={
                lecturerQuery.trim() === ''
                  ? NO_LECTURERS
                  : `No lecturers without an account match "${lecturerQuery.trim()}".`
              }
            />
          </FormField>
        )}

        <FormField
          label="Username"
          required
          error={errors.username?.message}
          hint={
            role === 'Admin'
              ? 'Letters, digits, dots, hyphens and underscores.'
              : 'Usually the student or staff number, which is what people type to sign in.'
          }
        >
          <Input
            {...register('username')}
            autoComplete="off"
            spellCheck={false}
            maxLength={64}
            className="font-mono"
          />
        </FormField>
        <FormField label="Display name" required error={errors.displayName?.message}>
          <Input {...register('displayName')} autoComplete="off" maxLength={200} />
        </FormField>
        <FormField label="Email" error={errors.email?.message} hint="Optional.">
          <Input {...register('email')} type="email" autoComplete="off" maxLength={256} />
        </FormField>
        <FormField
          label="Temporary password"
          error={errors.temporaryPassword?.message}
          hint="Optional. Leave it blank to generate a 16-character password."
        >
          <PasswordInput {...register('temporaryPassword')} autoComplete="new-password" />
        </FormField>
        <FormError>{formError}</FormError>
        <DialogFooter>
          <DialogClose asChild>
            <Button variant="secondary">Cancel</Button>
          </DialogClose>
          <Button type="submit" loading={isSubmitting}>
            Provision account
          </Button>
        </DialogFooter>
      </form>
      <CreateStudentDialog
        open={createStudentOpen}
        onOpenChange={setCreateStudentOpen}
        offerProvision={false}
        onCreated={(row) => {
          setStudentText(`${row.studentNumber} ${row.fullName}`)
          chooseStudent(row)
        }}
      />
    </>
  )
}

export interface ProvisionAccountDialogProps {
  open: boolean
  onOpenChange: (open: boolean) => void
  /** Preselects the student (from the student support page). */
  initialStudent?: AdminStudentRow | null
}

/**
 * "Provision account" (05-frontend.md section 10, `/admin/accounts`): role first, then the student
 * or lecturer it is for (Admin: nobody), an optional temporary password, and the password shown
 * once in `TemporaryPasswordDialog`.
 */
export function ProvisionAccountDialog({
  open,
  onOpenChange,
  initialStudent = null,
}: ProvisionAccountDialogProps) {
  const [credentials, setCredentials] = useState<{
    username: string
    password: string
    isDemo: boolean
  } | null>(null)

  return (
    <>
      <Dialog open={open} onOpenChange={onOpenChange}>
        <DialogContent
          title="Provision account"
          description="Creates a sign-in for an existing student or lecturer, or a new administrator."
        >
          <ProvisionForm
            initialStudent={initialStudent}
            onDone={(result) => {
              onOpenChange(false)
              setCredentials({
                username: result.account.username,
                password: result.temporaryPassword,
                isDemo: result.account.isDemo,
              })
            }}
          />
        </DialogContent>
      </Dialog>
      <TemporaryPasswordDialog credentials={credentials} onClose={() => setCredentials(null)} />
    </>
  )
}
