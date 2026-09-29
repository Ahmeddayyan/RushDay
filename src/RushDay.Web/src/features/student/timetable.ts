import type { TimetableEntry, Weekday } from '@/api/types/common'

import { minutesOf, WEEKDAYS, weekdayLabel, WORKING_DAYS, type ZonedMoment } from './time'

/** Timetable arithmetic shared by the grid, the agenda, the dashboard card and the ICS export. */

export const KIND_LABEL: Record<TimetableEntry['kind'], string> = {
  lecture: 'Lecture',
  lab: 'Lab',
}

export function entryKey(entry: TimetableEntry): string {
  return `${entry.moduleCode}-${entry.day}-${entry.startTime}-${entry.room}`
}

export function byStartTime(a: TimetableEntry, b: TimetableEntry): number {
  return minutesOf(a.startTime) - minutesOf(b.startTime) || a.moduleCode.localeCompare(b.moduleCode)
}

/** Monday to Friday, plus a weekend day only when a class falls on it. */
export function timetableDays(entries: readonly TimetableEntry[]): Weekday[] {
  return WEEKDAYS.filter(
    (day) => WORKING_DAYS.includes(day) || entries.some((entry) => entry.day === day),
  )
}

export function entriesOn(entries: readonly TimetableEntry[], day: Weekday): TimetableEntry[] {
  return entries.filter((entry) => entry.day === day).sort(byStartTime)
}

/** The hours the grid shows: 09:00 to 18:00 at least, widened to fit every class. */
export function gridHours(entries: readonly TimetableEntry[]): number[] {
  let first = 9
  let last = 18
  for (const entry of entries) {
    first = Math.min(first, Math.floor(minutesOf(entry.startTime) / 60))
    last = Math.max(last, Math.ceil(minutesOf(entry.endTime) / 60))
  }
  return Array.from({ length: Math.max(1, last - first) }, (_, index) => first + index)
}

export interface PlacedEntry {
  entry: TimetableEntry
  /** Which side-by-side lane of its overlap group (0-based). */
  lane: number
  /** How many lanes its overlap group needs. */
  lanes: number
}

/**
 * Lays one day's classes out in lanes: classes whose times overlap share the width of the column
 * (05-frontend.md section 10: "overlaps split the cell").
 */
export function placeDay(entries: readonly TimetableEntry[]): PlacedEntry[] {
  const sorted = [...entries].sort(byStartTime)
  const placed: PlacedEntry[] = []
  let group: PlacedEntry[] = []
  let laneEnds: number[] = []
  let groupEnd = -1

  const closeGroup = () => {
    const lanes = Math.max(1, laneEnds.length)
    for (const item of group) item.lanes = lanes
    placed.push(...group)
    group = []
    laneEnds = []
  }

  for (const entry of sorted) {
    const start = minutesOf(entry.startTime)
    const end = Math.max(start + 1, minutesOf(entry.endTime))
    if (group.length > 0 && start >= groupEnd) closeGroup()
    let lane = laneEnds.findIndex((laneEnd) => laneEnd <= start)
    if (lane === -1) {
      lane = laneEnds.length
      laneEnds.push(end)
    } else {
      laneEnds[lane] = end
    }
    group.push({ entry, lane, lanes: 1 })
    groupEnd = group.length === 1 ? end : Math.max(groupEnd, end)
  }
  if (group.length > 0) closeGroup()
  return placed
}

export interface NextClasses {
  /** "Today", "Tomorrow" or the weekday. */
  label: string
  day: Weekday
  entries: TimetableEntry[]
}

/**
 * Today's remaining classes, else the next day with classes (looking a full week ahead, so today's
 * finished classes come round again next week).
 */
export function nextClasses(
  timetable: readonly TimetableEntry[],
  now: Pick<ZonedMoment, 'weekday' | 'minutes'>,
): NextClasses | null {
  const start = WEEKDAYS.indexOf(now.weekday)
  for (let offset = 0; offset <= 7; offset += 1) {
    const day = WEEKDAYS[(start + offset) % 7] ?? 'monday'
    const entries = entriesOn(timetable, day).filter(
      (entry) => offset !== 0 || minutesOf(entry.endTime) > now.minutes,
    )
    if (entries.length > 0) {
      const label = offset === 0 ? 'Today' : offset === 1 ? 'Tomorrow' : weekdayLabel(day)
      return { label, day, entries }
    }
  }
  return null
}
