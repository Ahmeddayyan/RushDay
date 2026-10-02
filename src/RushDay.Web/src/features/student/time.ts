import { useEffect, useState } from 'react'

import type { Weekday } from '@/api/types/common'
import { DEFAULT_TIME_ZONE } from '@/lib/format'

/**
 * Wall-clock helpers for the timetable, the dashboard's "next classes" and the greeting. Timetable
 * times ("09:00") are the institution's local time; "today" and "now" are read in the institution's
 * zone (D26) on the server's clock, so a laptop in another zone or with a wrong clock still sees the
 * right day.
 */

export const WEEKDAYS: readonly Weekday[] = [
  'monday',
  'tuesday',
  'wednesday',
  'thursday',
  'friday',
  'saturday',
  'sunday',
]

export const WORKING_DAYS: readonly Weekday[] = WEEKDAYS.slice(0, 5)

const labels: Record<Weekday, string> = {
  monday: 'Monday',
  tuesday: 'Tuesday',
  wednesday: 'Wednesday',
  thursday: 'Thursday',
  friday: 'Friday',
  saturday: 'Saturday',
  sunday: 'Sunday',
}

/** "Monday" */
export function weekdayLabel(day: Weekday): string {
  return labels[day]
}

/** "Mon" */
export function shortWeekdayLabel(day: Weekday): string {
  return labels[day].slice(0, 3)
}

/** Minutes after midnight of an "HH:mm" time; 0 when malformed. */
export function minutesOf(time: string): number {
  const match = /^(\d{1,2}):(\d{2})$/.exec(time.trim())
  if (!match) return 0
  return Number(match[1]) * 60 + Number(match[2])
}

/** 540 → "09:00" */
export function formatMinutes(minutes: number): string {
  const hours = Math.floor(minutes / 60)
  const rest = minutes % 60
  return `${String(hours).padStart(2, '0')}:${String(rest).padStart(2, '0')}`
}

/** "09:00–11:00" */
export function timeRange(start: string, end: string): string {
  return `${start}–${end}`
}

/** A moment as the institution's wall clock reads it. */
export interface ZonedMoment {
  year: number
  /** 1..12 */
  month: number
  /** 1..31 */
  day: number
  weekday: Weekday
  hour: number
  /** Minutes after midnight. */
  minutes: number
}

const fromShort: Record<string, Weekday> = {
  Mon: 'monday',
  Tue: 'tuesday',
  Wed: 'wednesday',
  Thu: 'thursday',
  Fri: 'friday',
  Sat: 'saturday',
  Sun: 'sunday',
}

const partsFormatters = new Map<string, Intl.DateTimeFormat>()

function partsFormatter(timeZone: string): Intl.DateTimeFormat {
  let cached = partsFormatters.get(timeZone)
  if (!cached) {
    const options: Intl.DateTimeFormatOptions = {
      weekday: 'short',
      year: 'numeric',
      month: 'numeric',
      day: 'numeric',
      hour: 'numeric',
      minute: 'numeric',
      hourCycle: 'h23',
    }
    try {
      cached = new Intl.DateTimeFormat('en-GB', { ...options, timeZone })
    } catch {
      cached = new Intl.DateTimeFormat('en-GB', { ...options, timeZone: DEFAULT_TIME_ZONE })
    }
    partsFormatters.set(timeZone, cached)
  }
  return cached
}

/** The institution's wall clock at `instant` (milliseconds since the epoch). */
export function zonedMoment(instant: number, timeZone: string = DEFAULT_TIME_ZONE): ZonedMoment {
  const parts = partsFormatter(timeZone).formatToParts(new Date(instant))
  const get = (type: Intl.DateTimeFormatPartTypes) =>
    parts.find((part) => part.type === type)?.value ?? ''
  const hour = Number(get('hour')) % 24
  const minute = Number(get('minute'))
  return {
    year: Number(get('year')),
    month: Number(get('month')),
    day: Number(get('day')),
    weekday: fromShort[get('weekday')] ?? 'monday',
    hour,
    minutes: hour * 60 + minute,
  }
}

/** Days from `from` forward to the next `to` (0 when they are the same day). */
export function daysUntil(from: Weekday, to: Weekday): number {
  return (WEEKDAYS.indexOf(to) - WEEKDAYS.indexOf(from) + 7) % 7
}

/** The calendar date `days` after (year, month, day), as numbers. */
export function addDays(
  year: number,
  month: number,
  day: number,
  days: number,
): { year: number; month: number; day: number } {
  const date = new Date(Date.UTC(year, month - 1, day + days))
  return { year: date.getUTCFullYear(), month: date.getUTCMonth() + 1, day: date.getUTCDate() }
}

/** "Good morning" before 12:00, "Good afternoon" before 18:00, then "Good evening". */
export function greeting(hour: number): string {
  if (hour < 12) return 'Good morning'
  if (hour < 18) return 'Good afternoon'
  return 'Good evening'
}

/** The current time in milliseconds, updated every `intervalMs` (30 s by default). */
export function useNow(intervalMs = 30_000): number {
  const [now, setNow] = useState(() => Date.now())
  useEffect(() => {
    const timer = setInterval(() => setNow(Date.now()), intervalMs)
    return () => clearInterval(timer)
  }, [intervalMs])
  return now
}

/**
 * The institution's wall clock on the server's time (`offsetMs` from `useServerClock`), refreshed
 * every 30 s so "today" and the timetable's "now" line move on while the page stays open.
 */
export function useZonedNow(timeZone: string, offsetMs = 0): ZonedMoment {
  const now = useNow()
  return zonedMoment(now + offsetMs, timeZone)
}
