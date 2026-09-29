import { describe, expect, it } from 'vitest'

import { makeTimetableSlot } from '@/test/handlers/student'

import {
  buildTimetableIcs,
  escapeText,
  firstOccurrence,
  foldLine,
  ICS_WEEKS,
  icsFileName,
} from './ics'

// Monday 28 September 2026, 08:00 in London (07:00 UTC, BST).
const NOW = Date.parse('2026-09-28T07:00:00.000Z')

const options = {
  now: NOW,
  timeZone: 'Europe/London',
  calendarName: 'Autumn 2026/27 timetable',
  semester: 'autumn' as const,
  academicYear: '2026/27',
}

function unfold(ics: string): string[] {
  return ics.replaceAll('\r\n ', '').split('\r\n')
}

describe('buildTimetableIcs', () => {
  it('writes one weekly VEVENT per class with SUMMARY, LOCATION and COUNT=12', () => {
    const ics = buildTimetableIcs(
      [
        makeTimetableSlot(),
        makeTimetableSlot({
          moduleCode: 'CS3099',
          moduleTitle: 'Software Engineering Project',
          day: 'thursday',
          startTime: '14:00',
          endTime: '16:00',
          room: 'C-Lab2',
          kind: 'lab',
        }),
      ],
      options,
    )
    const lines = unfold(ics)

    expect(ics.startsWith('BEGIN:VCALENDAR\r\n')).toBe(true)
    expect(ics.endsWith('END:VCALENDAR\r\n')).toBe(true)
    expect(lines.filter((line) => line === 'BEGIN:VEVENT')).toHaveLength(2)
    expect(lines).toContain('SUMMARY:CS3001 Distributed Systems (Lecture)')
    expect(lines).toContain('SUMMARY:CS3099 Software Engineering Project (Lab)')
    expect(lines).toContain('LOCATION:B-201')
    expect(lines).toContain('LOCATION:C-Lab2')
    expect(lines.filter((line) => line === `RRULE:FREQ=WEEKLY;COUNT=${ICS_WEEKS}`)).toHaveLength(2)
    expect(ICS_WEEKS).toBe(12)
    expect(lines).toContain('X-WR-CALNAME:Autumn 2026/27 timetable')
  })

  it('starts on the next matching weekday in the institution zone, today if the class is still ahead', () => {
    const lines = unfold(
      buildTimetableIcs(
        [
          makeTimetableSlot({ startTime: '09:00', endTime: '11:00' }),
          makeTimetableSlot({ moduleCode: 'MA1001', startTime: '07:00', endTime: '08:00' }),
          makeTimetableSlot({ moduleCode: 'PH1002', day: 'wednesday' }),
        ],
        options,
      ),
    )
    // Monday 09:00 has not started at 08:00: today.
    expect(lines).toContain('DTSTART;TZID=Europe/London:20260928T090000')
    expect(lines).toContain('DTEND;TZID=Europe/London:20260928T110000')
    // Monday 07:00 already started: next Monday.
    expect(lines).toContain('DTSTART;TZID=Europe/London:20261005T070000')
    // Wednesday: this Wednesday.
    expect(lines).toContain('DTSTART;TZID=Europe/London:20260930T090000')
    // DTSTAMP is the instant in UTC.
    expect(lines).toContain('DTSTAMP:20260928T070000Z')
  })

  it('names the file after the semester and year', () => {
    expect(icsFileName('spring', '2026/27')).toBe('rushday-timetable-spring-2026-27.ics')
  })
})

describe('ICS text rules', () => {
  it('escapes commas, semicolons, backslashes and line breaks', () => {
    expect(escapeText('Lab; bring a laptop, charger\\cable\nand notes')).toBe(
      'Lab\\; bring a laptop\\, charger\\\\cable\\nand notes',
    )
  })

  it('folds lines longer than 75 octets', () => {
    const line = `SUMMARY:${'x'.repeat(200)}`
    const folded = foldLine(line)
    const parts = folded.split('\r\n')
    expect(parts.length).toBeGreaterThan(1)
    expect(parts.every((part) => new TextEncoder().encode(part).length <= 75)).toBe(true)
    expect(folded.replaceAll('\r\n ', '')).toBe(line)
    expect(foldLine('SHORT:line')).toBe('SHORT:line')
  })

  it('computes the first occurrence across a month end', () => {
    // Friday 30 October 2026 at 12:00 UTC (GMT again): the next Monday is 2 November.
    expect(
      firstOccurrence(
        { day: 'monday', startTime: '09:00' },
        Date.parse('2026-10-30T12:00:00.000Z'),
      ),
    ).toEqual({ year: 2026, month: 11, day: 2 })
  })
})
