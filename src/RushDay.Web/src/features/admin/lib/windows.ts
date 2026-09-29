import { DAY_MS, toApiInstant, zonedInputToMs } from '@/lib/zonedTime'

/** The three instants of a window as `datetime-local` text in the institution's zone. */
export interface WindowDraft {
  opensAt: string
  closesAt: string
  withdrawalDeadlineAt: string
}

export type WindowErrors = Partial<Record<keyof WindowDraft, string>>

/**
 * Mirrors `window-dates-invalid` (02-api.md section 8.5): every instant set, opens before closes,
 * and the withdrawal deadline not before closes. Returns the instants in API form when valid.
 */
export function validateWindow(
  draft: WindowDraft,
  timeZone: string,
): { errors: WindowErrors; instants: WindowDraft | null } {
  const errors: WindowErrors = {}
  const opens = zonedInputToMs(draft.opensAt, timeZone)
  const closes = zonedInputToMs(draft.closesAt, timeZone)
  const deadline = zonedInputToMs(draft.withdrawalDeadlineAt, timeZone)
  if (opens === null) errors.opensAt = 'Enter the date and time enrolment opens.'
  if (closes === null) errors.closesAt = 'Enter the date and time enrolment closes.'
  if (deadline === null) errors.withdrawalDeadlineAt = 'Enter the withdrawal deadline.'
  if (opens !== null && closes !== null && opens >= closes) {
    errors.closesAt = 'Closes must be after opens.'
  }
  if (closes !== null && deadline !== null && deadline < closes) {
    errors.withdrawalDeadlineAt = "The withdrawal deadline can't be before closes."
  }
  if (Object.keys(errors).length > 0 || opens === null || closes === null || deadline === null) {
    return { errors, instants: null }
  }
  return {
    errors,
    instants: {
      opensAt: toApiInstant(opens),
      closesAt: toApiInstant(closes),
      withdrawalDeadlineAt: toApiInstant(deadline),
    },
  }
}

/** "Open now for 7 days": opens now, closes in a week, deadline kept unless it would fall before. */
export function openNowFor7Days(
  window: { withdrawalDeadlineAt: string },
  now: number,
): WindowDraft {
  const closes = now + 7 * DAY_MS
  const deadline = Math.max(Date.parse(window.withdrawalDeadlineAt), closes)
  return {
    opensAt: toApiInstant(now),
    closesAt: toApiInstant(closes),
    withdrawalDeadlineAt: toApiInstant(deadline),
  }
}

/** "Close now": closes now; opens is moved back a minute if it was later, the deadline kept or now. */
export function closeNow(
  window: { opensAt: string; withdrawalDeadlineAt: string },
  now: number,
): WindowDraft {
  const opens = Math.min(Date.parse(window.opensAt), now - 60_000)
  const deadline = Math.max(Date.parse(window.withdrawalDeadlineAt), now)
  return {
    opensAt: toApiInstant(opens),
    closesAt: toApiInstant(now),
    withdrawalDeadlineAt: toApiInstant(deadline),
  }
}
