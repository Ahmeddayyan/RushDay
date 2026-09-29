import { useId, type ReactNode } from 'react'
import { Check, GraduationCap } from 'lucide-react'
import { Link } from 'react-router'

import type { ModuleSummary, MyEnrolment } from '@/api/types/common'
import type { CompletedModule } from '@/api/types/student'
import { Badge, Button, buttonStyles, Spinner, SupportLink } from '@/components/ui'
import { cn } from '@/lib/cn'
import { formatDate, formatDateTime, formatSemester } from '@/lib/format'

import { placesLeft } from '../copy'
import { CREDIT_LIMIT, deriveEnrolState, type EnrolState } from '../enrolState'
import { completedResultText } from '../grades'
import type { EnrolControl } from '../hooks/useEnrol'

import { WithdrawDialog } from './WithdrawDialog'

export interface EnrolButtonProps {
  module: ModuleSummary
  /** The student's row for this module, of any year. */
  row: MyEnrolment | undefined
  /** The settings year from `['public','status']`, when known. */
  currentYear: string | null | undefined
  /** This year's active credits in the module's semester. */
  creditsUsed: number
  enrolmentsFresh: boolean
  /** `useEnrol(module.code)` of the card or page. */
  control: EnrolControl
  onWithdraw: () => void
  /**
   * The module's entry in `['student','dashboard'].completed`: `undefined` when the dashboard is not
   * known, `null` when it is known and has no entry.
   */
  completed: CompletedModule | null | undefined
  timeZone: string
  size?: 'md' | 'lg'
  className?: string
}

/** Keeps "Enrol" and "Enrolling…" (and the disabled labels) the same width, so nothing jumps. */
const actionWidth = 'min-w-36'

function EnrolledTag({ size }: { size: 'md' | 'lg' }) {
  return (
    <span
      className={cn(
        buttonStyles({ variant: 'secondary', size }),
        'cursor-default hover:bg-surface',
        actionWidth,
      )}
    >
      <Check aria-hidden="true" className="size-4 text-success" />
      Enrolled
    </span>
  )
}

interface View {
  action: ReactNode
  caption: ReactNode
  /** One sentence for the polite live region when something happens that the student did not see coming. */
  announce?: string
}

/**
 * The enrol control for one module with its caption (05-frontend.md section 10, `EnrolButton`
 * states, and section 6.3 for pending, "filled while you were enrolling" and busy). The caption is
 * always visible and linked to the button through `aria-describedby`.
 */
export function EnrolButton({
  module,
  row,
  currentYear,
  creditsUsed,
  enrolmentsFresh,
  control,
  onWithdraw,
  completed,
  timeZone,
  size = 'md',
  className,
}: EnrolButtonProps) {
  const captionId = useId()
  const state: EnrolState = deriveEnrolState({
    module,
    row,
    currentYear,
    creditsUsed,
    enrolmentsFresh,
  })
  const semester = formatSemester(module.semester)
  const enrolLabel = state.again ? 'Enrol again' : 'Enrol'

  const primary = (label: string, disabled = false) => (
    <Button
      size={size}
      className={actionWidth}
      disabled={disabled}
      aria-describedby={captionId}
      aria-label={`${label} on ${module.code}`}
      onClick={control.enrol}
    >
      {label}
    </Button>
  )
  const blocked = (label: string) => (
    <Button
      size={size}
      variant="secondary"
      className={actionWidth}
      disabled
      aria-describedby={captionId}
    >
      {label}
    </Button>
  )

  const describeState = (): View => {
    switch (state.kind) {
      case 'enrolled':
        return {
          action: (
            <>
              <EnrolledTag size={size} />
              <WithdrawDialog
                code={module.code}
                title={module.title}
                semester={module.semester}
                windowOpen={module.enrolmentState === 'open'}
                windowClosesAt={module.windowClosesAt}
                withdrawalDeadlineAt={row?.withdrawalDeadlineAt ?? module.withdrawalDeadlineAt}
                timeZone={timeZone}
                onConfirm={onWithdraw}
                size={size === 'lg' ? 'md' : 'sm'}
              />
            </>
          ),
          caption: row?.withdrawalDeadlineAt
            ? `Withdrawal deadline ${formatDateTime(row.withdrawalDeadlineAt, timeZone)}`
            : 'You can withdraw until the withdrawal deadline.',
        }
      case 'enrolledResults':
        return {
          action: <EnrolledTag size={size} />,
          caption: (
            <>
              Marks recorded; to withdraw, <SupportLink />.
            </>
          ),
        }
      case 'enrolledDeadline':
        return {
          action: <EnrolledTag size={size} />,
          caption: row?.withdrawalDeadlineAt
            ? `Withdrawal deadline passed ${formatDateTime(row.withdrawalDeadlineAt, timeZone)}.`
            : 'The withdrawal deadline has passed.',
        }
      case 'completed':
        return {
          action: (
            <Badge variant="success" icon={GraduationCap}>
              Completed {row?.academicYear}
            </Badge>
          ),
          caption:
            completed === undefined ? (
              <>
                Your mark is on your{' '}
                <Link
                  to="/student/results"
                  className="font-medium text-primary underline underline-offset-2"
                >
                  Results page
                </Link>
                .
              </>
            ) : completed === null ? (
              'Result not yet published'
            ) : (
              completedResultText(completed)
            ),
        }
      case 'inactive':
        return { action: blocked('Not running'), caption: `${module.code} is no longer running.` }
      case 'full':
        return {
          action: blocked('Full'),
          caption:
            control.filledAt !== null
              ? 'Filled while you were enrolling'
              : 'Full. Places free up when students withdraw; there is no waiting list yet.',
          ...(control.filledAt !== null
            ? { announce: `${module.code} filled while you were enrolling.` }
            : {}),
        }
      case 'notYetOpen':
        return {
          action: blocked('Not open yet'),
          caption: module.windowOpensAt
            ? `Enrolment for ${semester} opens ${formatDateTime(module.windowOpensAt, timeZone)}.`
            : `Enrolment for ${semester} has not opened yet.`,
        }
      case 'closed':
        return {
          action: blocked('Enrolment closed'),
          caption: module.windowClosesAt
            ? `Enrolment for ${semester} closed on ${formatDateTime(module.windowClosesAt, timeZone)}.`
            : `Enrolment for ${semester} is closed.`,
        }
      case 'noWindow':
        return {
          action: blocked('Enrolment closed'),
          caption: `Enrolment dates for ${semester} have not been announced yet.`,
        }
      case 'overCredits':
        return {
          action: state.disabled ? blocked('Over credit limit') : primary(enrolLabel),
          caption: `That would take you over ${CREDIT_LIMIT} credits for ${semester}.`,
        }
      case 'enrolAgain':
        return {
          action: primary('Enrol again'),
          caption: row?.withdrawnAt
            ? `Withdrawn ${formatDate(row.withdrawnAt, timeZone)}`
            : placesLeft(module.placesRemaining),
        }
      case 'enrol':
        return { action: primary('Enrol'), caption: placesLeft(module.placesRemaining) }
    }
  }

  let view: View
  if (control.isPending) {
    view = {
      action: (
        <Button
          size={size}
          className={actionWidth}
          disabled
          aria-busy="true"
          aria-describedby={captionId}
        >
          <Spinner size="sm" decorative className="text-current" />
          Enrolling…
        </Button>
      ),
      caption: 'Waiting for the portal to confirm your place.',
      announce: `Enrolling on ${module.code}…`,
    }
  } else if (control.busySeconds > 0 && !state.kind.startsWith('enrolled')) {
    view = {
      action: primary(enrolLabel, true),
      caption: `The portal is busy. Try again in ${control.busySeconds}s.`,
      announce: 'The portal is busy. Try again in a moment.',
    }
  } else {
    view = describeState()
  }

  return (
    <div className={cn('flex min-w-0 flex-col gap-2', className)}>
      <div className="flex flex-wrap items-center gap-2">{view.action}</div>
      <p id={captionId} className="text-sm text-muted">
        {view.caption}
      </p>
      <p role="status" className="sr-only">
        {view.announce ?? ''}
      </p>
    </div>
  )
}
