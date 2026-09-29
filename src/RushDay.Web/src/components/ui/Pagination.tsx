import { ChevronLeft, ChevronRight } from 'lucide-react'

import { cn } from '@/lib/cn'
import { formatNumber } from '@/lib/format'

import { Button } from './Button'

/**
 * The API pages no deeper than row 10,000 (02-api.md section 1, S6 review E14): past it,
 * `page x pageSize` is 400 `validation`, because an OFFSET that deep costs a full scan per page.
 */
const MAX_PAGED_ROWS = 10_000

export interface PaginationProps {
  /** 1-based, as the API pages. */
  page: number
  pageSize: number
  total: number
  onPageChange: (page: number) => void
  /** "students", "accounts": used in "Showing 1–25 of 1,234 students". */
  itemLabel?: string
  /** Disables the buttons while the next page loads (the previous page stays on screen). */
  busy?: boolean
  /** What reaches the rows past 10,000 on this page ("Narrow the search", "use the export"). */
  beyondLimitHint?: string
  className?: string
}

/** Server-side paging controls with a live summary of what is on screen. */
export function Pagination({
  page,
  pageSize,
  total,
  onPageChange,
  itemLabel = 'results',
  busy = false,
  beyondLimitHint = 'Narrow the search or the filters to reach the rest.',
  className,
}: PaginationProps) {
  const pageCount = Math.max(1, Math.ceil(total / pageSize))
  const lastReachable = Math.max(1, Math.floor(MAX_PAGED_ROWS / pageSize))
  const atLimit = pageCount > lastReachable && page >= lastReachable
  const first = total === 0 ? 0 : (page - 1) * pageSize + 1
  const last = Math.min(total, page * pageSize)

  return (
    <nav
      aria-label="Pagination"
      className={cn('flex flex-wrap items-center justify-between gap-3', className)}
    >
      <p className="text-sm text-muted tabular-nums" aria-live="polite">
        {total === 0
          ? `No ${itemLabel}`
          : `Showing ${formatNumber(first)}–${formatNumber(last)} of ${formatNumber(total)} ${itemLabel}`}
      </p>
      <div className="flex items-center gap-2">
        <Button
          variant="secondary"
          size="sm"
          onClick={() => onPageChange(page - 1)}
          disabled={busy || page <= 1}
          aria-label="Previous page"
        >
          <ChevronLeft aria-hidden="true" className="size-4" />
          <span className="max-sm:sr-only">Previous</span>
        </Button>
        <span className="min-w-20 text-center text-sm text-muted tabular-nums">
          Page {formatNumber(page)} of {formatNumber(pageCount)}
        </span>
        <Button
          variant="secondary"
          size="sm"
          onClick={() => onPageChange(page + 1)}
          disabled={busy || page >= pageCount || atLimit}
          aria-label="Next page"
        >
          <span className="max-sm:sr-only">Next</span>
          <ChevronRight aria-hidden="true" className="size-4" />
        </Button>
      </div>
      {atLimit && (
        <p className="w-full text-right text-sm text-muted">
          Paging stops at the first {formatNumber(MAX_PAGED_ROWS)} {itemLabel}. {beyondLimitHint}
        </p>
      )}
    </nav>
  )
}
