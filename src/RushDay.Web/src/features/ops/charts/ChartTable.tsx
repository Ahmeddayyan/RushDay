import type { ReactNode } from 'react'

import { Table, TableBody, TableCell, TableHead, TableHeaderCell, TableRow } from '@/components/ui'

/**
 * The table twin of a chart (05-frontend.md section 11): identical numbers to the chart it sits
 * beside in `ChartCard`'s Chart/Table tabs, so `*.test.tsx` files can assert "table twin equals
 * chart data" by reading the same `rows`/`columns` the chart itself was given.
 */
export interface ChartTableColumn<Row> {
  key: string
  header: string
  numeric?: boolean
  render: (row: Row) => ReactNode
}

export interface ChartTableProps<Row> {
  /** Visually hidden: the adjacent ChartCard title already names the table. */
  caption: string
  columns: ChartTableColumn<Row>[]
  rows: Row[]
  rowKey: (row: Row, index: number) => string
}

export function ChartTable<Row>({ caption, columns, rows, rowKey }: ChartTableProps<Row>) {
  return (
    <Table caption={caption} captionHidden>
      <TableHead>
        <TableRow>
          {columns.map((column) => (
            <TableHeaderCell key={column.key} numeric={column.numeric ?? false}>
              {column.header}
            </TableHeaderCell>
          ))}
        </TableRow>
      </TableHead>
      <TableBody>
        {rows.map((row, index) => (
          <TableRow key={rowKey(row, index)}>
            {columns.map((column) => (
              <TableCell key={column.key} numeric={column.numeric ?? false} label={column.header}>
                {column.render(row)}
              </TableCell>
            ))}
          </TableRow>
        ))}
      </TableBody>
    </Table>
  )
}
