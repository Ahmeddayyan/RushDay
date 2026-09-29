import {
  createContext,
  useCallback,
  useContext,
  useEffect,
  useId,
  useRef,
  useState,
  type ComponentProps,
  type ReactNode,
} from 'react'
import { ArrowDown, ArrowUp, ArrowUpDown } from 'lucide-react'

import { cn } from '@/lib/cn'

/**
 * Data tables (05-frontend.md section 9.3). Always a real <table> with a <caption> (visually hidden
 * when a heading already names the table). Below 640 px the rows stack as cards, each cell prefixed
 * by its column name from `label` (`data-label`); explicit ARIA table roles keep the semantics that
 * `display: block` would otherwise strip in some browsers. `scroll="x"` opts a wide table (audit,
 * accounts) into horizontal scrolling instead, with a fade on the edge that still has content.
 * Every scroller is `relative`: visually hidden text in a cell is `position: absolute`, and without a
 * positioned scroller it escapes the scroll clip and widens the whole page on a phone.
 * Cells are vertically centred, so text lines up with the buttons and chips in the same row.
 */

interface TableContextValue {
  stacked: boolean
  compact: boolean
}

const TableContext = createContext<TableContextValue>({ stacked: false, compact: false })

export interface TableProps extends Omit<ComponentProps<'table'>, 'children'> {
  caption: ReactNode
  captionHidden?: boolean
  /** 'stack' (default): cards below 640 px. 'x': keep columns and scroll sideways. */
  mode?: 'stack' | 'x'
  /** 'compact': 12 px instead of 16 px of padding either side of a cell, for tables of many columns. */
  density?: 'default' | 'compact'
  children: ReactNode
  wrapperClassName?: string
}

function useScrollFade() {
  const ref = useRef<HTMLDivElement>(null)
  const [fade, setFade] = useState({ start: false, end: false })

  const measure = useCallback(() => {
    const node = ref.current
    if (!node) return
    const maxScroll = node.scrollWidth - node.clientWidth
    setFade({ start: node.scrollLeft > 1, end: maxScroll - node.scrollLeft > 1 })
  }, [])

  useEffect(() => {
    const node = ref.current
    if (!node) return
    measure()
    node.addEventListener('scroll', measure, { passive: true })
    const observer = typeof ResizeObserver === 'undefined' ? null : new ResizeObserver(measure)
    observer?.observe(node)
    // The scroller keeps its size when rows arrive; the table inside it is what grows wider.
    if (node.firstElementChild) observer?.observe(node.firstElementChild)
    return () => {
      node.removeEventListener('scroll', measure)
      observer?.disconnect()
    }
  }, [measure])

  return { ref, fade }
}

export function Table({
  caption,
  captionHidden = false,
  mode = 'stack',
  density = 'default',
  className,
  wrapperClassName,
  children,
  ...props
}: TableProps) {
  const stacked = mode === 'stack'
  const compact = density === 'compact'
  const captionId = useId()
  const { ref, fade } = useScrollFade()

  const table = (
    <table
      role={stacked ? 'table' : undefined}
      className={cn(
        'w-full border-collapse text-left text-sm',
        stacked && 'max-sm:block',
        className,
      )}
      {...props}
    >
      <caption
        id={captionId}
        className={cn(
          captionHidden ? 'sr-only' : 'px-4 py-3 text-left text-sm font-medium text-muted',
          stacked && !captionHidden && 'max-sm:block max-sm:px-0',
        )}
      >
        {caption}
      </caption>
      {children}
    </table>
  )

  return (
    <TableContext value={{ stacked, compact }}>
      <div
        className={cn(
          'relative w-full rounded-lg border border-border bg-surface',
          stacked && 'max-sm:rounded-none max-sm:border-0 max-sm:bg-transparent',
          wrapperClassName,
        )}
      >
        {stacked ? (
          <div className="relative w-full sm:overflow-x-auto">{table}</div>
        ) : (
          <>
            <div
              ref={ref}
              role="region"
              aria-labelledby={captionId}
              tabIndex={0}
              className="relative w-full overflow-x-auto rounded-lg focus-visible:outline-offset-[-2px]"
            >
              {table}
            </div>
            <div
              aria-hidden="true"
              className={cn(
                'pointer-events-none absolute inset-y-0 left-0 w-12 rounded-l-lg bg-linear-to-r from-surface to-transparent transition-opacity',
                fade.start ? 'opacity-100' : 'opacity-0',
              )}
            />
            <div
              aria-hidden="true"
              className={cn(
                'pointer-events-none absolute inset-y-0 right-0 w-12 rounded-r-lg bg-linear-to-l from-surface to-transparent transition-opacity',
                fade.end ? 'opacity-100' : 'opacity-0',
              )}
            />
          </>
        )}
      </div>
    </TableContext>
  )
}

export function TableHead({ className, ...props }: ComponentProps<'thead'>) {
  const { stacked } = useContext(TableContext)
  return (
    <thead
      role={stacked ? 'rowgroup' : undefined}
      className={cn('bg-surface-2', stacked && 'max-sm:sr-only', className)}
      {...props}
    />
  )
}

export function TableBody({ className, ...props }: ComponentProps<'tbody'>) {
  const { stacked } = useContext(TableContext)
  return (
    <tbody
      role={stacked ? 'rowgroup' : undefined}
      className={cn(
        'divide-y divide-border',
        stacked && 'max-sm:block max-sm:space-y-3 max-sm:divide-y-0',
        className,
      )}
      {...props}
    />
  )
}

export function TableRow({ className, ...props }: ComponentProps<'tr'>) {
  const { stacked } = useContext(TableContext)
  return (
    <tr
      role={stacked ? 'row' : undefined}
      className={cn(
        'transition-colors hover:bg-surface-2/60',
        stacked &&
          'max-sm:block max-sm:rounded-lg max-sm:border max-sm:border-border max-sm:bg-surface max-sm:px-4 max-sm:py-2 max-sm:shadow-card',
        className,
      )}
      {...props}
    />
  )
}

export type SortState = 'ascending' | 'descending' | 'none'

export interface TableHeaderCellProps extends ComponentProps<'th'> {
  /** Makes the column sortable: `aria-sort` goes on this <th> and a button inside toggles it. */
  sort?: SortState
  onSort?: () => void
  /** The column's name in the sort button's label; defaults to the text children. */
  sortLabel?: string
  numeric?: boolean
}

const sortStateText: Record<SortState, string> = {
  ascending: 'sorted ascending',
  descending: 'sorted descending',
  none: 'not sorted',
}

export function TableHeaderCell({
  className,
  scope = 'col',
  sort,
  onSort,
  sortLabel,
  numeric = false,
  children,
  ...props
}: TableHeaderCellProps) {
  const { stacked, compact } = useContext(TableContext)
  const sortable = sort !== undefined && onSort !== undefined
  const name = sortLabel ?? (typeof children === 'string' ? children : 'this column')
  const SortIcon = sort === 'ascending' ? ArrowUp : sort === 'descending' ? ArrowDown : ArrowUpDown

  return (
    <th
      scope={scope}
      role={stacked ? (scope === 'row' ? 'rowheader' : 'columnheader') : undefined}
      aria-sort={sortable ? sort : undefined}
      className={cn(
        'border-b border-border py-2.5 text-xs font-semibold tracking-wide whitespace-nowrap text-muted uppercase',
        compact ? 'px-3' : 'px-4',
        numeric && 'text-right',
        className,
      )}
      {...props}
    >
      {sortable ? (
        <button
          type="button"
          onClick={onSort}
          aria-label={`Sort by ${name}, currently ${sortStateText[sort]}`}
          className={cn(
            '-mx-1.5 inline-flex cursor-pointer items-center gap-1.5 rounded-sm px-1.5 py-0.5 uppercase hover:text-text',
            numeric && 'flex-row-reverse',
          )}
        >
          {children}
          <SortIcon
            aria-hidden="true"
            className={cn('size-3.5', sort === 'none' && 'opacity-60')}
          />
        </button>
      ) : (
        children
      )}
    </th>
  )
}

export interface TableCellProps extends ComponentProps<'td'> {
  /** Right-aligned, tabular figures. */
  numeric?: boolean
  /** Column name shown before the value when rows stack on small screens. */
  label?: string
}

export function TableCell({ className, numeric = false, label, ...props }: TableCellProps) {
  const { stacked, compact } = useContext(TableContext)
  return (
    <td
      role={stacked ? 'cell' : undefined}
      data-label={label}
      className={cn(
        'py-3 align-middle text-text',
        compact ? 'px-3' : 'px-4',
        numeric && 'text-right tabular-nums whitespace-nowrap',
        stacked &&
          'max-sm:flex max-sm:items-baseline max-sm:justify-between max-sm:gap-4 max-sm:px-0 max-sm:py-1.5 max-sm:text-right',
        stacked &&
          label &&
          'max-sm:before:shrink-0 max-sm:before:text-left max-sm:before:font-sans max-sm:before:font-medium max-sm:before:text-muted max-sm:before:content-[attr(data-label)]',
        className,
      )}
      {...props}
    />
  )
}
