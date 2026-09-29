import type { Role, Semester } from '@/api/types/common'
import type { PublicStatus, SupportInfo } from '@/api/types/public'

/**
 * Formatting helpers (05-frontend.md section 10 conventions). Every instant is stored in UTC and
 * shown in the institution's time zone from `['public','status']` (D26), absolute always, with the
 * zone named for enrolment windows and publication instants; the relative form goes in a tooltip.
 * Intl only: no date library (05 section 1).
 */

export const DEFAULT_TIME_ZONE = 'Europe/London'
const LOCALE = 'en-GB'

type Instant = string | number | Date

function toDate(instant: Instant): Date {
  return instant instanceof Date ? instant : new Date(instant)
}

const formatterCache = new Map<string, Intl.DateTimeFormat>()

function formatter(timeZone: string, options: Intl.DateTimeFormatOptions): Intl.DateTimeFormat {
  const key = timeZone + JSON.stringify(options)
  let cached = formatterCache.get(key)
  if (!cached) {
    try {
      cached = new Intl.DateTimeFormat(LOCALE, { ...options, timeZone })
    } catch {
      // An unknown zone id must never break a page: fall back to the default zone.
      cached = new Intl.DateTimeFormat(LOCALE, { ...options, timeZone: DEFAULT_TIME_ZONE })
    }
    formatterCache.set(key, cached)
  }
  return cached
}

/** "28 September 2026" */
export function formatDate(instant: Instant, timeZone: string = DEFAULT_TIME_ZONE): string {
  return formatter(timeZone, { day: 'numeric', month: 'long', year: 'numeric' }).format(
    toDate(instant),
  )
}

/** "28 Sep 2026", for tables. */
export function formatShortDate(instant: Instant, timeZone: string = DEFAULT_TIME_ZONE): string {
  return formatter(timeZone, { day: 'numeric', month: 'short', year: 'numeric' }).format(
    toDate(instant),
  )
}

/** "09:00" (24-hour, as UK timetables are written). */
export function formatTime(instant: Instant, timeZone: string = DEFAULT_TIME_ZONE): string {
  return formatter(timeZone, { hour: '2-digit', minute: '2-digit', hourCycle: 'h23' }).format(
    toDate(instant),
  )
}

/** The zone's short name at that instant: "BST" in summer and "GMT" in winter for Europe/London. */
export function zoneLabel(instant: Instant, timeZone: string = DEFAULT_TIME_ZONE): string {
  const parts = formatter(timeZone, { timeZoneName: 'short' }).formatToParts(toDate(instant))
  return parts.find((part) => part.type === 'timeZoneName')?.value ?? timeZone
}

/** "28 September 2026 at 09:00 (BST)"; `zone: false` drops the zone where nothing is ambiguous. */
export function formatDateTime(
  instant: Instant,
  timeZone: string = DEFAULT_TIME_ZONE,
  { zone = true }: { zone?: boolean } = {},
): string {
  const text = `${formatDate(instant, timeZone)} at ${formatTime(instant, timeZone)}`
  return zone ? `${text} (${zoneLabel(instant, timeZone)})` : text
}

const relativeFormatter = new Intl.RelativeTimeFormat(LOCALE, { numeric: 'auto' })

const relativeSteps: [Intl.RelativeTimeFormatUnit, number][] = [
  ['year', 365 * 24 * 3600],
  ['month', 30 * 24 * 3600],
  ['week', 7 * 24 * 3600],
  ['day', 24 * 3600],
  ['hour', 3600],
  ['minute', 60],
]

/** "in 3 days", "2 hours ago", "now"; for tooltips next to an absolute date. */
export function formatRelative(instant: Instant, now: number = Date.now()): string {
  const seconds = Math.round((toDate(instant).getTime() - now) / 1000)
  for (const [unit, size] of relativeSteps) {
    if (Math.abs(seconds) >= size) return relativeFormatter.format(Math.round(seconds / size), unit)
  }
  return 'just now'
}

const numberFormatter = new Intl.NumberFormat(LOCALE)

/** "20,000" */
export function formatNumber(value: number): string {
  return numberFormatter.format(value)
}

/** "70.0" (one decimal, as averages are shown). */
export function formatDecimal(value: number, digits = 1): string {
  return value.toLocaleString(LOCALE, {
    minimumFractionDigits: digits,
    maximumFractionDigits: digits,
  })
}

/** "Autumn" / "Spring" */
export function formatSemester(semester: Semester): string {
  return semester === 'autumn' ? 'Autumn' : 'Spring'
}

/** "Student" / "Lecturer" / "Administrator" */
export function formatRole(role: Role): string {
  return role === 'Admin' ? 'Administrator' : role
}

export function capitalise(text: string): string {
  return text.length === 0 ? text : text.charAt(0).toUpperCase() + text.slice(1)
}

/**
 * The plain-text form of `SupportLink` (05 section 6.4 `{support}`): "contact the academic office at
 * {email}" or "contact the academic office ({url})" when support details are set, else "contact the
 * academic office".
 */
export function supportText(support: SupportInfo | null | undefined): string {
  if (support?.email) return `contact the academic office at ${support.email}`
  if (support?.url) return `contact the academic office (${support.url})`
  return 'contact the academic office'
}

/** `supportText` read from a `['public','status']` response. */
export function supportLink(status: Pick<PublicStatus, 'institution'> | null | undefined): string {
  return supportText(status?.institution.support)
}
