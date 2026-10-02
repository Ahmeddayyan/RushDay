import {
  Ban,
  CalendarCheck,
  CalendarClock,
  CalendarX,
  CircleCheck,
  CircleSlash,
  Clock,
  FlaskConical,
  KeyRound,
  Lock,
  LogOut,
  Pencil,
  ShieldCheck,
  UserX,
} from 'lucide-react'

import type { StudentAccountState } from '@/api/types/admin'
import type { AccountView, WindowInfo } from '@/api/types/common'
import { Badge } from '@/components/ui'
import { formatDateTime } from '@/lib/format'

import { isAdministratorLock } from '../lib/accounts'

/**
 * Status chips of the administrator area. Every chip is soft background + icon + words, never
 * colour alone (05-frontend.md section 9.3).
 */

/** The chips of one account: state, then must-change, two-step and demo. */
export function AccountStateChips({
  account,
  timeZone,
}: {
  account: AccountView
  timeZone: string
}) {
  return (
    <span className="flex flex-wrap gap-1.5">
      {account.state === 'disabled' && (
        <Badge variant="danger" icon={Ban}>
          Disabled
        </Badge>
      )}
      {account.state === 'locked' &&
        (isAdministratorLock(account.lockoutEnd) || account.lockoutEnd === null ? (
          <Badge variant="warning" icon={Lock}>
            Locked by administrator
          </Badge>
        ) : (
          <Badge variant="warning" icon={Lock}>
            Locked until {formatDateTime(account.lockoutEnd, timeZone)}
          </Badge>
        ))}
      {account.state === 'active' && (
        <Badge variant="success" icon={CircleCheck}>
          Active
        </Badge>
      )}
      {account.mustChangePassword && (
        <Badge variant="info" icon={KeyRound}>
          Must change password
        </Badge>
      )}
      {account.mfaEnabled && (
        <Badge variant="success" icon={ShieldCheck}>
          Two-step on
        </Badge>
      )}
      {account.isDemo && (
        <Badge variant="neutral" icon={FlaskConical}>
          Demo
        </Badge>
      )}
    </span>
  )
}

/** A student's account state in the students list. */
export function StudentAccountChip({ state }: { state: StudentAccountState }) {
  switch (state) {
    case 'active':
      return (
        <Badge variant="success" icon={CircleCheck}>
          Account active
        </Badge>
      )
    case 'locked':
      return (
        <Badge variant="warning" icon={Lock}>
          Locked
        </Badge>
      )
    case 'disabled':
      return (
        <Badge variant="danger" icon={Ban}>
          Disabled
        </Badge>
      )
    default:
      return (
        <Badge variant="neutral" icon={CircleSlash}>
          No account
        </Badge>
      )
  }
}

/** "Left" on a student or lecturer who has left the university. */
export function LeftBadge({ leftAt, timeZone }: { leftAt: string; timeZone: string }) {
  return (
    <Badge variant="neutral" icon={LogOut} title={`Left on ${formatDateTime(leftAt, timeZone)}`}>
      Left
    </Badge>
  )
}

export function WindowStateChip({ state }: { state: WindowInfo['state'] }) {
  if (state === 'open') {
    return (
      <Badge variant="success" icon={CalendarCheck}>
        Open
      </Badge>
    )
  }
  if (state === 'notYetOpen') {
    return (
      <Badge variant="info" icon={CalendarClock}>
        Not yet open
      </Badge>
    )
  }
  return (
    <Badge variant="neutral" icon={CalendarX}>
      Closed
    </Badge>
  )
}

/** A publication in the history table: Scheduled (calendar) or Live (check). */
export function PublicationStateChip({ state }: { state: 'scheduled' | 'live' }) {
  return state === 'scheduled' ? (
    <Badge variant="warning" icon={CalendarClock}>
      Scheduled
    </Badge>
  ) : (
    <Badge variant="success" icon={CircleCheck}>
      Live
    </Badge>
  )
}

/** One grade's status (Draft / Submitted / Published), as the marks chip draws them. */
export function GradeStatusChip({
  status,
  live = true,
}: {
  status: 'draft' | 'submitted' | 'published'
  /** A Published grade whose instant has not come yet reads "Scheduled". */
  live?: boolean
}) {
  if (status === 'draft') {
    return (
      <Badge variant="neutral" icon={Pencil}>
        Draft
      </Badge>
    )
  }
  if (status === 'submitted') {
    return (
      <Badge variant="info" icon={Clock}>
        Submitted
      </Badge>
    )
  }
  return live ? (
    <Badge variant="success" icon={CircleCheck}>
      Published
    </Badge>
  ) : (
    <Badge variant="warning" icon={CalendarClock}>
      Scheduled
    </Badge>
  )
}

export function EnrolmentStatusChip({ status }: { status: 'active' | 'withdrawn' }) {
  return status === 'active' ? (
    <Badge variant="success" icon={CircleCheck}>
      Active
    </Badge>
  ) : (
    <Badge variant="neutral" icon={UserX}>
      Withdrawn
    </Badge>
  )
}
