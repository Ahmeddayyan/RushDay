import type { ComponentProps } from 'react'

import { cn } from '@/lib/cn'

/**
 * Semantic table primitives. <Table> wraps the element in a horizontally scrollable container so
 * wide data never stretches the page on a 360 px screen.
 */
export function Table({ className, ...props }: ComponentProps<'table'>) {
  return (
    <div className="w-full overflow-x-auto rounded-lg border border-border bg-surface">
      <table className={cn('w-full border-collapse text-left text-sm', className)} {...props} />
    </div>
  )
}

export function TableCaption({ className, ...props }: ComponentProps<'caption'>) {
  return <caption className={cn('px-4 py-3 text-left text-sm text-muted', className)} {...props} />
}

export function TableHead({ className, ...props }: ComponentProps<'thead'>) {
  return <thead className={cn('bg-background/60', className)} {...props} />
}

export function TableBody({ className, ...props }: ComponentProps<'tbody'>) {
  return <tbody className={cn('divide-y divide-border', className)} {...props} />
}

export function TableRow({ className, ...props }: ComponentProps<'tr'>) {
  return <tr className={cn('transition-colors hover:bg-border/30', className)} {...props} />
}

export function TableHeaderCell({ className, scope = 'col', ...props }: ComponentProps<'th'>) {
  return (
    <th
      scope={scope}
      className={cn(
        'border-b border-border px-4 py-2.5 text-xs font-semibold tracking-wide text-muted uppercase',
        className,
      )}
      {...props}
    />
  )
}

export interface TableCellProps extends ComponentProps<'td'> {
  /** Right-aligned, tabular figures. */
  numeric?: boolean
}

export function TableCell({ className, numeric = false, ...props }: TableCellProps) {
  return (
    <td
      className={cn(
        'px-4 py-3 align-top',
        numeric && 'text-right tabular-nums whitespace-nowrap',
        className,
      )}
      {...props}
    />
  )
}
