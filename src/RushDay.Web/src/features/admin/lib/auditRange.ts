import { startOfZonedDay, toApiInstant, toZonedInput, zonedInputToMs } from '@/lib/zonedTime'

/** The audit log's filters as URL parameters (05-frontend.md section 10: "URL-synced"). */
export const AUDIT_FILTER_KEYS = [
  'actor',
  'action',
  'studentNumber',
  'moduleCode',
  'range',
  'from',
  'to',
] as const

export type AuditFilterKey = (typeof AUDIT_FILTER_KEYS)[number]
export type AuditFilterValues = Record<AuditFilterKey, string>

/** The audit log's date presets (05-frontend.md section 10): Today, 7 days, 30 days, custom. */
export type AuditRange = '' | 'today' | '7d' | '30d' | 'custom'

const PRESET_DAYS: Record<'today' | '7d' | '30d', number> = { today: 1, '7d': 7, '30d': 30 }

/**
 * A preset as instants: from the start of the first day to the start of tomorrow, in the
 * institution's zone. Both bounds are sent, so a 7- or 30-day export gets the 50,000-row cap.
 */
export function presetRange(
  range: 'today' | '7d' | '30d',
  now: number,
  timeZone: string,
): { from: string; to: string } {
  return {
    from: toApiInstant(startOfZonedDay(now, timeZone, 1 - PRESET_DAYS[range])),
    to: toApiInstant(startOfZonedDay(now, timeZone, 1)),
  }
}

/** A custom day (`YYYY-MM-DD`, the zone's calendar) as the instant it starts, or null. */
export function dayStart(day: string, timeZone: string, addDays = 0): string | null {
  const ms = zonedInputToMs(`${day}T00:00`, timeZone)
  if (ms === null) return null
  return toApiInstant(addDays === 0 ? ms : startOfZonedDay(ms, timeZone, addDays))
}

/** The calendar day an instant falls on in the zone, for a date input. */
export function dayOf(instant: string, timeZone: string, addDays = 0): string {
  const ms = Date.parse(instant)
  if (Number.isNaN(ms)) return ''
  return toZonedInput(addDays === 0 ? ms : startOfZonedDay(ms, timeZone, addDays), timeZone).slice(
    0,
    10,
  )
}
