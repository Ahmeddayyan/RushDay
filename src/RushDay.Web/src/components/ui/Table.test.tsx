import { render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { useState } from 'react'
import { describe, expect, it } from 'vitest'

import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeaderCell,
  TableRow,
  type SortState,
} from './Table'

function ModulesTable({ mode }: { mode?: 'stack' | 'x' }) {
  const [sort, setSort] = useState<SortState>('none')
  return (
    <Table caption="Modules" captionHidden {...(mode ? { mode } : {})}>
      <TableHead>
        <TableRow>
          <TableHeaderCell
            sort={sort}
            onSort={() => setSort(sort === 'ascending' ? 'descending' : 'ascending')}
          >
            Code
          </TableHeaderCell>
          <TableHeaderCell>Title</TableHeaderCell>
          <TableHeaderCell numeric>Credits</TableHeaderCell>
        </TableRow>
      </TableHead>
      <TableBody>
        <TableRow>
          <TableCell label="Code">CS3099</TableCell>
          <TableCell label="Title">Software Engineering Project</TableCell>
          <TableCell label="Credits" numeric>
            15
          </TableCell>
        </TableRow>
      </TableBody>
    </Table>
  )
}

describe('Table', () => {
  it('is a real table with a caption, column headers and cells', () => {
    render(<ModulesTable />)
    const table = screen.getByRole('table', { name: 'Modules' })
    expect(within(table).getAllByRole('columnheader')).toHaveLength(3)
    expect(within(table).getByRole('cell', { name: 'CS3099' })).toBeInTheDocument()
    expect(screen.getByText('Modules')).toHaveClass('sr-only')
  })

  it('puts aria-sort on the header cell and names the sort button with its state', async () => {
    render(<ModulesTable />)
    const header = screen.getByRole('columnheader', { name: /Code/ })
    expect(header).toHaveAttribute('aria-sort', 'none')
    const button = within(header).getByRole('button', {
      name: 'Sort by Code, currently not sorted',
    })
    expect(button).not.toHaveAttribute('aria-sort')

    await userEvent.click(button)
    expect(header).toHaveAttribute('aria-sort', 'ascending')
    expect(within(header).getByRole('button')).toHaveAccessibleName(
      'Sort by Code, currently sorted ascending',
    )

    await userEvent.click(within(header).getByRole('button'))
    expect(header).toHaveAttribute('aria-sort', 'descending')
    // Columns that are not sortable carry no aria-sort.
    expect(screen.getByRole('columnheader', { name: 'Title' })).not.toHaveAttribute('aria-sort')
  })

  it('stacks rows as cards on small screens, labelling each cell with its column', () => {
    render(<ModulesTable />)
    const table = screen.getByRole('table')
    expect(table).toHaveAttribute('role', 'table')
    expect(table).toHaveClass('max-sm:block')
    const cell = screen.getByRole('cell', { name: 'Software Engineering Project' })
    expect(cell).toHaveAttribute('data-label', 'Title')
    expect(cell.className).toContain('max-sm:before:content-[attr(data-label)]')
    expect(screen.getAllByRole('rowgroup')[0]).toHaveClass('max-sm:sr-only')
  })

  it('opts into horizontal scrolling with a focusable, named region instead', () => {
    render(<ModulesTable mode="x" />)
    const region = screen.getByRole('region', { name: 'Modules' })
    expect(region).toHaveAttribute('tabindex', '0')
    expect(screen.getByRole('table')).not.toHaveClass('max-sm:block')
    expect(screen.getByRole('cell', { name: 'CS3099' }).className).not.toContain('max-sm:flex')
  })
})
