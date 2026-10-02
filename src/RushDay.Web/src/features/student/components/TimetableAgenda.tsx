import { useId } from 'react'

import type { TimetableEntry, Weekday } from '@/api/types/common'
import { Tabs, TabsContent, TabsList, TabsTrigger } from '@/components/ui'

import { shortWeekdayLabel, timeRange, weekdayLabel } from '../time'
import { entriesOn, entryKey, KIND_LABEL, timetableDays } from '../timetable'

export interface TimetableAgendaProps {
  entries: readonly TimetableEntry[]
  today: Weekday | null
  /**
   * `tabs`: one day at a time behind day tabs, today first (phones). `list`: every day as a heading
   * and a list (the screen-reader twin of the grid on wider screens).
   */
  mode: 'tabs' | 'list'
}

function AgendaList({ entries, day }: { entries: TimetableEntry[]; day: Weekday }) {
  if (entries.length === 0) {
    return <p className="py-6 text-center text-sm text-muted">No classes on {weekdayLabel(day)}.</p>
  }
  return (
    <ul className="flex flex-col gap-2">
      {entries.map((entry) => (
        <li
          key={entryKey(entry)}
          className="flex gap-3 rounded-lg border border-border bg-surface p-3 shadow-card"
        >
          <span className="w-24 shrink-0 text-sm font-medium text-text tabular-nums">
            {timeRange(entry.startTime, entry.endTime)}
          </span>
          <span className="min-w-0 text-sm">
            <span className="block font-medium text-text">
              <span className="font-mono">{entry.moduleCode}</span> {entry.moduleTitle}
            </span>
            <span className="block text-muted">
              {KIND_LABEL[entry.kind]} · {entry.room}
            </span>
          </span>
        </li>
      ))}
    </ul>
  )
}

/**
 * The timetable as a list per day (05-frontend.md section 10, `/student/timetable`): day tabs that
 * open on today below 768 px, and a visually hidden list of every day for screen readers above it.
 */
export function TimetableAgenda({ entries, today, mode }: TimetableAgendaProps) {
  const headingId = useId()
  const days = timetableDays(entries)

  if (mode === 'list') {
    return (
      <section aria-labelledby={headingId}>
        <h2 id={headingId}>Classes by day</h2>
        {days.map((day) => (
          <div key={day}>
            <h3>
              {weekdayLabel(day)}
              {day === today ? ' (today)' : ''}
            </h3>
            <AgendaList entries={entriesOn(entries, day)} day={day} />
          </div>
        ))}
      </section>
    )
  }

  const firstWithClasses = days.find((day) => entries.some((entry) => entry.day === day))
  const initial = today && days.includes(today) ? today : (firstWithClasses ?? days[0] ?? 'monday')

  return (
    <Tabs defaultValue={initial}>
      <TabsList aria-label="Day" className="flex w-full">
        {days.map((day) => (
          <TabsTrigger key={day} value={day} className="flex-1 justify-center px-2">
            <span aria-hidden="true">{shortWeekdayLabel(day)}</span>
            <span className="sr-only">
              {weekdayLabel(day)}
              {day === today ? ', today' : ''}
            </span>
            {day === today && (
              <span aria-hidden="true" className="size-1.5 rounded-full bg-primary" />
            )}
          </TabsTrigger>
        ))}
      </TabsList>
      {days.map((day) => (
        <TabsContent key={day} value={day}>
          <AgendaList entries={entriesOn(entries, day)} day={day} />
        </TabsContent>
      ))}
    </Tabs>
  )
}
