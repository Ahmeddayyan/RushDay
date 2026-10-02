import type { AdminStudentRecord } from '@/api/types/admin'
import { toast } from '@/lib/toast'

import { useMarkStudentLeft } from '../hooks/useStudents'

import { ReasonDialog } from './ReasonDialog'

/**
 * "Mark as left" (05-frontend.md section 10): withdraws this year's active enrolments and disables
 * the linked account, with a reason of at least 10 characters.
 */
export function MarkStudentLeftDialog({
  student,
  activeEnrolments,
  open,
  onOpenChange,
}: {
  student: AdminStudentRecord
  /** Active enrolments in the current academic year: the ones this withdraws. */
  activeEnrolments: number
  open: boolean
  onOpenChange: (open: boolean) => void
}) {
  const leave = useMarkStudentLeft(student.studentNumber)
  const enrolments = `${activeEnrolments} ${activeEnrolments === 1 ? 'enrolment' : 'enrolments'}`

  return (
    <ReasonDialog
      open={open}
      onOpenChange={onOpenChange}
      title={`Mark ${student.fullName} as left?`}
      description={`Withdraws ${enrolments} this year and disables the account.`}
      confirmLabel="Mark as left"
      reasonHint="For example the date and route of withdrawal from the programme."
      onConfirm={async (reason) => {
        const result = await leave.mutateAsync(reason)
        toast.success(
          `${student.studentNumber} is marked as left. ${result.withdrawn} ${
            result.withdrawn === 1 ? 'enrolment' : 'enrolments'
          } withdrawn.`,
        )
      }}
    />
  )
}
