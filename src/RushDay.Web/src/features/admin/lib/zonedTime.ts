import { DEFAULT_TIME_ZONE } from '@/lib/format'

/**
 * Wall-clock time in the institution's zone (D26). Administrators type windows and publication
 * instants as the registry reads them ("09:00 in Europe/London"), in `<input type="datetime-local">`,
 * whatever zone their own computer is in; the API takes UTC instants with three fractional digits.
 * Intl only, no date library (05-frontend.md section 1).
 */

export const DAY_MS = 24 * 60 * 60 * 1000
export const PUBLISH_MAX_DAYS = 90

interface WallClock {
  year: number
  month: number
  day: number
  hour: number
  minute: number
  second: number
}

const partsFormatters = new Map<string, Intl.DateTimeFormat>()

function partsFormatter(timeZone: string): Intl.DateTimeFormat {
  let cached = partsFormatters.get(timeZone)
  if (!cached) {
    const options: Intl.DateTimeFormatOptions = {
      year: 'numeric',
      month: '2-digit',
      day: '2-digit',
      hour: '2-digit',
      minute: '2-digit',
      second: '2-digit',
      hourCycle: 'h23',
    }
    try {
      cached = new Intl.DateTimeFormat('en-US', { ...options, timeZone })
    } catch {
      cached = new Intl.DateTimeFormat('en-US', { ...options, timeZone: DEFAULT_TIME_ZONE })
    }
    partsFormatters.set(timeZone, cached)
  }
  return cached
}

function wallClock(ms: number, timeZone: string): WallClock {
  const values: Record<string, number> = {}
  for (const part of partsFormatter(timeZone).formatToParts(new Date(ms))) {
    if (part.type !== 'literal') values[part.type] = Number(part.value)
  }
  return {
    year: values.year ?? 1970,
    month: values.month ?? 1,
    day: values.day ?? 1,
    hour: (values.hour ?? 0) % 24,
    minute: values.minute ?? 0,
    second: values.second ?? 0,
  }
}

/** The zone's offset from UTC at an instant, in milliseconds (BST: +3,600,000). */
export function zoneOffsetMs(ms: number, timeZone: string): number {
  const wall = wallClock(ms, timeZone)
  const asUtc = Date.UTC(wall.year, wall.month - 1, wall.day, wall.hour, wall.minute, wall.second)
  return asUtc - (ms - (((ms % 1000) + 1000) % 1000))
}

const pad = (value: number, width = 2) => String(value).padStart(width, '0')

/** An instant as the `datetime-local` value `YYYY-MM-DDTHH:mm` in the zone. */
export function toZonedInput(instant: string | number, timeZone: string): string {
  const ms = typeof instant === 'number' ? instant : Date.parse(instant)
  if (Number.isNaN(ms)) return ''
  const wall = wallClock(ms, timeZone)
  return `${pad(wall.year, 4)}-${pad(wall.month)}-${pad(wall.day)}T${pad(wall.hour)}:${pad(wall.minute)}`
}

const INPUT_PATTERN = /^(\d{4})-(\d{2})-(\d{2})T(\d{2}):(\d{2})(?::(\d{2}))?$/

/**
 * A `datetime-local` value read as wall-clock time in the zone → epoch milliseconds, or null when
 * the text is not a complete date and time. Around a clock change the offset is re-read at the
 * result, so 09:00 on the last Sunday of October is 09:00 GMT, not BST.
 */
export function zonedInputToMs(value: string, timeZone: string): number | null {
  const match = INPUT_PATTERN.exec(value.trim())
  if (!match) return null
  const [, y, mo, d, h, mi, s] = match
  const guess = Date.UTC(
    Number(y),
    Number(mo) - 1,
    Number(d),
    Number(h),
    Number(mi),
    Number(s ?? 0),
  )
  if (Number.isNaN(guess)) return null
  const first = zoneOffsetMs(guess, timeZone)
  let result = guess - first
  const second = zoneOffsetMs(result, timeZone)
  if (second !== first) result = guess - second
  return result
}

/** ISO-8601 UTC with exactly three fractional digits, as the API writes and reads instants. */
export function toApiInstant(ms: number): string {
  return new Date(ms).toISOString()
}

/** `zonedInputToMs` straight to the API's instant format. */
export function zonedInputToInstant(value: string, timeZone: string): string | null {
  const ms = zonedInputToMs(value, timeZone)
  return ms === null ? null : toApiInstant(ms)
}

/** Midnight of the zone's calendar day containing `ms`, moved by `addDays` days. */
export function startOfZonedDay(ms: number, timeZone: string, addDays = 0): number {
  const wall = wallClock(ms, timeZone)
  const shifted = new Date(Date.UTC(wall.year, wall.month - 1, wall.day + addDays))
  const value = `${pad(shifted.getUTCFullYear(), 4)}-${pad(shifted.getUTCMonth() + 1)}-${pad(shifted.getUTCDate())}T00:00`
  return zonedInputToMs(value, timeZone) ?? ms
}

/** The next 09:00 in the zone after `ms` (the results-day moment), for the publish picker's default. */
export function nextNineAm(ms: number, timeZone: string): number {
  const today = startOfZonedDay(ms, timeZone)
  const todayNine = zonedInputToMs(`${toZonedInput(today, timeZone).slice(0, 10)}T09:00`, timeZone)
  if (todayNine !== null && todayNine > ms) return todayNine
  const tomorrow = startOfZonedDay(ms, timeZone, 1)
  return zonedInputToMs(`${toZonedInput(tomorrow, timeZone).slice(0, 10)}T09:00`, timeZone) ?? ms
}

/** The current time. A function, so components read the clock in handlers and effects, not render. */
export function currentTime(): number {
  return Date.now()
}
