import type { Semester, TimetableEntry } from '@/api/types/common'
import { DEFAULT_TIME_ZONE } from '@/lib/format'

import { addDays, daysUntil, minutesOf, zonedMoment } from './time'
import { KIND_LABEL } from './timetable'

/**
 * The timetable as an iCalendar file (RFC 5545), built in the browser (05-frontend.md section 10,
 * `/student/timetable`): one weekly VEVENT per class, `SUMMARY:{code} {title} ({kind})`,
 * `LOCATION:{room}`, `RRULE:FREQ=WEEKLY;COUNT=12`, first occurrence on the next matching weekday
 * (today when the class has not started yet). Times are the institution's wall-clock times, written
 * with `TZID={zone}` so calendars keep 09:00 at 09:00 across the October and March clock changes.
 */

export const ICS_WEEKS = 12

export interface TimetableIcsOptions {
  /** Now, in milliseconds on the server's clock. */
  now: number
  /** The institution's IANA zone, for example "Europe/London". */
  timeZone?: string
  /** "Autumn 2026/27 timetable" */
  calendarName: string
  semester: Semester
  academicYear: string
}

/** Escapes a TEXT value: backslash, semicolon, comma and line breaks. */
export function escapeText(value: string): string {
  return value
    .replaceAll('\\', '\\\\')
    .replaceAll(';', '\\;')
    .replaceAll(',', '\\,')
    .replaceAll(/\r\n|\r|\n/g, '\\n')
}

const encoder = new TextEncoder()

/** Folds a content line at 75 octets; continuation lines start with one space. */
export function foldLine(line: string): string {
  if (encoder.encode(line).length <= 75) return line
  const pieces: string[] = []
  let current = ''
  let currentBytes = 0
  for (const char of line) {
    const bytes = encoder.encode(char).length
    const limit = pieces.length === 0 ? 75 : 74
    if (currentBytes + bytes > limit) {
      pieces.push(current)
      current = ''
      currentBytes = 0
    }
    current += char
    currentBytes += bytes
  }
  pieces.push(current)
  return pieces.join('\r\n ')
}

const pad = (value: number, length = 2) => String(value).padStart(length, '0')

function utcStamp(ms: number): string {
  const date = new Date(ms)
  return (
    `${date.getUTCFullYear()}${pad(date.getUTCMonth() + 1)}${pad(date.getUTCDate())}` +
    `T${pad(date.getUTCHours())}${pad(date.getUTCMinutes())}${pad(date.getUTCSeconds())}Z`
  )
}

function localStamp(date: { year: number; month: number; day: number }, time: string): string {
  const minutes = minutesOf(time)
  return `${date.year}${pad(date.month)}${pad(date.day)}T${pad(Math.floor(minutes / 60))}${pad(minutes % 60)}00`
}

/** The calendar date of the first occurrence: the next matching weekday, today if still ahead. */
export function firstOccurrence(
  entry: Pick<TimetableEntry, 'day' | 'startTime'>,
  now: number,
  timeZone: string = DEFAULT_TIME_ZONE,
): { year: number; month: number; day: number } {
  const today = zonedMoment(now, timeZone)
  let offset = daysUntil(today.weekday, entry.day)
  if (offset === 0 && minutesOf(entry.startTime) <= today.minutes) offset = 7
  return addDays(today.year, today.month, today.day, offset)
}

/** "rushday-timetable-autumn-2026-27.ics" */
export function icsFileName(semester: Semester, academicYear: string): string {
  return `rushday-timetable-${semester}-${academicYear.replaceAll('/', '-')}.ics`
}

export function buildTimetableIcs(
  entries: readonly TimetableEntry[],
  options: TimetableIcsOptions,
): string {
  const timeZone = options.timeZone ?? DEFAULT_TIME_ZONE
  const stamp = utcStamp(options.now)
  const year = options.academicYear.replaceAll('/', '-')

  const lines = [
    'BEGIN:VCALENDAR',
    'VERSION:2.0',
    'PRODID:-//RushDay//Student timetable//EN',
    'CALSCALE:GREGORIAN',
    'METHOD:PUBLISH',
    `X-WR-CALNAME:${escapeText(options.calendarName)}`,
    `X-WR-TIMEZONE:${timeZone}`,
  ]

  for (const entry of entries) {
    const first = firstOccurrence(entry, options.now, timeZone)
    const kind = KIND_LABEL[entry.kind]
    lines.push(
      'BEGIN:VEVENT',
      `UID:${entry.moduleCode}-${entry.day}-${entry.startTime.replace(':', '')}-${entry.room.replaceAll(/[^A-Za-z0-9-]/g, '')}-${options.semester}-${year}@rushday`,
      `DTSTAMP:${stamp}`,
      `DTSTART;TZID=${timeZone}:${localStamp(first, entry.startTime)}`,
      `DTEND;TZID=${timeZone}:${localStamp(first, entry.endTime)}`,
      `RRULE:FREQ=WEEKLY;COUNT=${ICS_WEEKS}`,
      `SUMMARY:${escapeText(`${entry.moduleCode} ${entry.moduleTitle} (${kind})`)}`,
      `LOCATION:${escapeText(entry.room)}`,
      `DESCRIPTION:${escapeText(`${kind} for ${entry.moduleCode}, ${options.calendarName}.`)}`,
      'END:VEVENT',
    )
  }

  lines.push('END:VCALENDAR')
  return lines.map(foldLine).join('\r\n') + '\r\n'
}
