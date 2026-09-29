import type { TimetableEntry, Weekday } from '@/api/types/common'
import { cn } from '@/lib/cn'

import { formatMinutes, minutesOf, timeRange, weekdayLabel } from '../time'
import { entryKey, gridHours, KIND_LABEL, placeDay, timetableDays } from '../timetable'

export interface TimetableGridProps {
  entries: readonly TimetableEntry[]
  /** Id of the page heading that names the table. */
  labelledBy: string
  /** "{Semester} {academicYear} timetable" */
  caption: string
  /** Today in the institution's zone, highlighted; null when unknown. */
  today: Weekday | null
  /** Minutes after midnight now, for the "now" line; null to hide it. */
  nowMinutes: number | null
}

/** Height of one hour row in pixels; blocks are positioned against it. */
const HOUR_PX = 72

/**
 * The week as a real table (05-frontend.md section 10, `/student/timetable`): days are column
 * headers, hours are row headers, and each class sits in the cell of its start hour, stretched to
 * its length. Overlapping classes share the width of the column; today's column is highlighted and
 * a "now" line marks the time. Screen readers also get the agenda list rendered next to it.
 */
export function TimetableGrid({
  entries,
  labelledBy,
  caption,
  today,
  nowMinutes,
}: TimetableGridProps) {
  const days = timetableDays(entries)
  const hours = gridHours(entries)
  const placed = new Map(days.map((day) => [day, placeDay(entries.filter((e) => e.day === day))]))

  return (
    <div className="overflow-x-auto rounded-lg border border-border bg-surface shadow-card">
      <table
        aria-labelledby={labelledBy}
        className="w-full min-w-[640px] table-fixed border-collapse"
      >
        <caption className="sr-only">{caption}</caption>
        <thead>
          <tr>
            <th scope="col" className="w-20 border-b border-border bg-surface-2 px-3 py-2.5">
              <span className="sr-only">Time</span>
            </th>
            {days.map((day) => {
              const isToday = day === today
              return (
                <th
                  key={day}
                  scope="col"
                  aria-current={isToday ? 'date' : undefined}
                  className={cn(
                    'border-b border-l border-border px-3 py-2.5 text-left text-sm font-semibold',
                    isToday ? 'bg-primary-soft text-primary' : 'bg-surface-2 text-text',
                  )}
                >
                  {weekdayLabel(day)}
                  {isToday ? ' ' : null}
                  {isToday && <span className="ml-1 text-xs font-medium">Today</span>}
                </th>
              )
            })}
          </tr>
        </thead>
        <tbody>
          {hours.map((hour) => (
            <tr key={hour}>
              <th
                scope="row"
                className="border-t border-border px-3 pt-1.5 text-left align-top text-xs font-medium text-muted tabular-nums"
                style={{ height: HOUR_PX }}
              >
                {formatMinutes(hour * 60)}
              </th>
              {days.map((day) => {
                const isToday = day === today
                const starting = (placed.get(day) ?? []).filter(
                  ({ entry }) => Math.floor(minutesOf(entry.startTime) / 60) === hour,
                )
                const showNow =
                  isToday && nowMinutes !== null && Math.floor(nowMinutes / 60) === hour
                return (
                  <td
                    key={day}
                    className={cn(
                      'relative border-t border-l border-border p-0 align-top',
                      isToday && 'bg-primary-soft/40',
                    )}
                    style={{ height: HOUR_PX }}
                  >
                    {starting.map(({ entry, lane, lanes }) => {
                      const start = minutesOf(entry.startTime)
                      const end = Math.max(start + 15, minutesOf(entry.endTime))
                      return (
                        <div
                          key={entryKey(entry)}
                          className={cn(
                            'absolute z-10 overflow-hidden rounded-md border-l-4 px-2 py-1 text-xs leading-snug text-text shadow-card',
                            entry.kind === 'lab'
                              ? 'border-warning bg-warning-soft'
                              : 'border-primary bg-primary-soft',
                          )}
                          style={{
                            top: ((start - hour * 60) / 60) * HOUR_PX + 2,
                            height: ((end - start) / 60) * HOUR_PX - 4,
                            left: `calc(${(lane / lanes) * 100}% + 3px)`,
                            width: `calc(${100 / lanes}% - 6px)`,
                          }}
                        >
                          <p className="font-semibold">
                            <span className="font-mono">{entry.moduleCode}</span>{' '}
                            {entry.moduleTitle}
                          </p>
                          <p>
                            {KIND_LABEL[entry.kind]} · {entry.room}
                          </p>
                          <p className="tabular-nums">
                            {timeRange(entry.startTime, entry.endTime)}
                          </p>
                        </div>
                      )
                    })}
                    {showNow && (
                      <div
                        aria-hidden="true"
                        className="pointer-events-none absolute inset-x-0 z-20 flex items-center"
                        style={{ top: ((nowMinutes - hour * 60) / 60) * HOUR_PX }}
                      >
                        <span className="-ml-1 size-2 rounded-full bg-danger" />
                        <span className="h-0.5 flex-1 bg-danger" />
                      </div>
                    )}
                  </td>
                )
              })}
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  )
}
