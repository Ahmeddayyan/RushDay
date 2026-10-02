import { render, screen, within } from '@testing-library/react'
import { describe, expect, it } from 'vitest'

import { makeTimetableSlot } from '@/test/handlers/student'

import { TimetableAgenda } from './TimetableAgenda'
import { TimetableGrid } from './TimetableGrid'

const entries = [
  makeTimetableSlot(),
  makeTimetableSlot({
    moduleCode: 'CS3099',
    moduleTitle: 'Software Engineering Project',
    day: 'monday',
    startTime: '10:00',
    endTime: '12:00',
    room: 'C-104',
  }),
  makeTimetableSlot({
    moduleCode: 'MA2004',
    moduleTitle: 'Linear Algebra',
    day: 'wednesday',
    startTime: '14:00',
    endTime: '15:00',
    room: 'M-Lab1',
    kind: 'lab',
  }),
]

function renderGrid(props: Partial<Parameters<typeof TimetableGrid>[0]> = {}) {
  return render(
    <>
      <h1 id="timetable-heading">Timetable</h1>
      <TimetableGrid
        entries={entries}
        labelledBy="timetable-heading"
        caption="Autumn 2026/27 timetable"
        today="wednesday"
        nowMinutes={14 * 60 + 30}
        {...props}
      />
    </>,
  )
}

describe('TimetableGrid', () => {
  it('is a table labelled by the page heading, with day column headers and time row headers', () => {
    renderGrid()
    const table = screen.getByRole('table', { name: 'Timetable' })
    expect(within(table).getByText('Autumn 2026/27 timetable').tagName).toBe('CAPTION')

    const columns = within(table).getAllByRole('columnheader')
    expect(columns.map((header) => header.textContent)).toEqual([
      'Time',
      'Monday',
      'Tuesday',
      'Wednesday Today',
      'Thursday',
      'Friday',
    ])
    columns.forEach((header) => expect(header).toHaveAttribute('scope', 'col'))

    const rows = within(table).getAllByRole('rowheader')
    expect(rows[0]).toHaveTextContent('09:00')
    expect(rows[rows.length - 1]).toHaveTextContent('17:00')
    rows.forEach((header) => expect(header).toHaveAttribute('scope', 'row'))
  })

  it('places each class in the cell of its day and start hour, with kind, room and time', () => {
    renderGrid()
    const table = screen.getByRole('table', { name: 'Timetable' })
    const nineOClock = within(table).getByRole('rowheader', { name: '09:00' }).closest('tr')
    const cells = within(nineOClock as HTMLElement).getAllByRole('cell')
    // Monday is the first day column.
    expect(cells[0]).toHaveTextContent('CS3001 Distributed Systems')
    expect(cells[0]).toHaveTextContent('Lecture · B-201')
    expect(cells[0]).toHaveTextContent('09:00–11:00')

    const two = within(table).getByRole('rowheader', { name: '14:00' }).closest('tr')
    expect(within(two as HTMLElement).getAllByRole('cell')[2]).toHaveTextContent(
      'MA2004 Linear AlgebraLab · M-Lab1',
    )
  })

  it('splits overlapping classes and marks today and now', () => {
    const { container } = renderGrid()
    const monday = [...container.querySelectorAll<HTMLElement>('td div.absolute')].filter((block) =>
      /CS3001|CS3099/.test(block.textContent ?? ''),
    )
    expect(monday.map((block) => block.style.width)).toEqual(['calc(50% - 6px)', 'calc(50% - 6px)'])
    expect(screen.getByRole('columnheader', { name: 'Wednesday Today' })).toHaveAttribute(
      'aria-current',
      'date',
    )
    // The "now" line is decorative; the agenda and the heading carry the meaning.
    expect(container.querySelector('[aria-hidden="true"] .bg-danger')).not.toBeNull()
  })

  it('adds a weekend day only when a class falls on it', () => {
    renderGrid({ entries: [...entries, makeTimetableSlot({ day: 'saturday' })], today: null })
    expect(screen.getByRole('columnheader', { name: 'Saturday' })).toBeInTheDocument()
    expect(screen.queryByRole('columnheader', { name: 'Sunday' })).not.toBeInTheDocument()
  })
})

describe('TimetableAgenda', () => {
  it('opens on today with a tab per day', () => {
    render(<TimetableAgenda entries={entries} today="wednesday" mode="tabs" />)
    const selected = screen.getByRole('tab', { selected: true })
    expect(selected).toHaveTextContent('Wednesday, today')
    expect(screen.getByRole('tabpanel')).toHaveTextContent('MA2004 Linear Algebra')
    expect(screen.getAllByRole('tab')).toHaveLength(5)
  })

  it('lists every day for screen readers', () => {
    render(<TimetableAgenda entries={entries} today="monday" mode="list" />)
    expect(screen.getByRole('heading', { name: 'Monday (today)' })).toBeInTheDocument()
    expect(screen.getByText('No classes on Tuesday.')).toBeInTheDocument()
  })
})
