import { toast } from '@/lib/toast'

import { useOverrideWithdraw } from '../hooks/useStudents'

import { ReasonDialog } from './ReasonDialog'

export interface WithdrawTarget {
  code: string
  title: string
  academicYear: string
  /** A submitted or published mark exists: the student stops seeing it. */
  hasResult: boolean
}

/**
 * `OverrideWithdrawDialog` (05-frontend.md section 10): withdraws one enrolment with a reason,
 * ignoring the withdrawal deadline and the results rule (02-api.md section 8.5).
 */
export function OverrideWithdrawDialog({
  studentNumber,
  studentName,
  target,
  onOpenChange,
}: {
  studentNumber: string
  studentName: string
  /** Null closes the dialog. */
  target: WithdrawTarget | null
  onOpenChange: (open: boolean) => void
}) {
  const withdraw = useOverrideWithdraw(studentNumber)
  return (
    <ReasonDialog
      open={target !== null}
      onOpenChange={onOpenChange}
      title={target ? `Withdraw ${studentName} from ${target.code}?` : ''}
      description={
        target ? (
          <>
            <p>
              Withdraws the {target.academicYear} enrolment on {target.code} {target.title} now; the
              withdrawal deadline doesn&apos;t apply to an administrator.
            </p>
            {target.hasResult && (
              <p>{studentName} has a mark for this module and will no longer see it.</p>
            )}
          </>
        ) : (
          ''
        )
      }
      confirmLabel="Withdraw"
      onConfirm={async (reason) => {
        if (!target) return
        await withdraw.mutateAsync({ code: target.code, reason })
        toast.success(`Withdrew ${studentNumber} from ${target.code}.`)
      }}
      errorContext={target ? { code: target.code } : {}}
    />
  )
}
