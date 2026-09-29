import type { Semester } from '@/api/types/common'
import {
  AlertDialog,
  AlertDialogAction,
  AlertDialogCancel,
  AlertDialogContent,
  AlertDialogFooter,
  AlertDialogTrigger,
  Button,
} from '@/components/ui'
import { formatDateTime, formatSemester } from '@/lib/format'

export interface WithdrawDialogProps {
  code: string
  title: string
  semester: Semester
  /** Enrolment for the module's semester is open now. */
  windowOpen: boolean
  windowClosesAt: string | null
  withdrawalDeadlineAt: string | null
  timeZone: string
  onConfirm: () => void
  size?: 'sm' | 'md'
}

/**
 * The withdrawal confirmation (05-frontend.md section 6.3): an AlertDialog whose description states
 * the consequence and whether the student could enrol again, with the destructive "Withdraw".
 */
export function WithdrawDialog({
  code,
  title,
  semester,
  windowOpen,
  windowClosesAt,
  withdrawalDeadlineAt,
  timeZone,
  onConfirm,
  size = 'md',
}: WithdrawDialogProps) {
  const closes = windowClosesAt ? formatDateTime(windowClosesAt, timeZone) : null
  const reEnrol = windowOpen
    ? closes
      ? `You can re-enrol while places remain until ${closes}.`
      : 'You can re-enrol while places remain.'
    : closes
      ? `Enrolment for ${formatSemester(semester)} closed on ${closes}, so you won't be able to re-enrol yourself.`
      : `Enrolment for ${formatSemester(semester)} is closed, so you won't be able to re-enrol yourself.`

  return (
    <AlertDialog>
      <AlertDialogTrigger asChild>
        <Button variant="ghost" size={size} aria-label={`Withdraw from ${code}`}>
          Withdraw
        </Button>
      </AlertDialogTrigger>
      <AlertDialogContent
        title={`Withdraw from ${code} ${title}`}
        description={
          <>
            <p>Withdraw from {code}? Your place is released immediately.</p>
            <p>{reEnrol}</p>
            {withdrawalDeadlineAt && (
              <p>Withdrawal deadline: {formatDateTime(withdrawalDeadlineAt, timeZone)}.</p>
            )}
          </>
        }
      >
        <AlertDialogFooter>
          <AlertDialogCancel>Cancel</AlertDialogCancel>
          <AlertDialogAction onClick={onConfirm}>Withdraw</AlertDialogAction>
        </AlertDialogFooter>
      </AlertDialogContent>
    </AlertDialog>
  )
}
