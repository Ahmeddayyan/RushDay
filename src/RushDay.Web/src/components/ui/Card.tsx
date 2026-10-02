import type { ComponentProps, ReactNode } from 'react'

import { cn } from '@/lib/cn'

export interface CardProps extends ComponentProps<'div'> {
  /** Drop the built-in padding, for a card whose content (a table) runs edge to edge. */
  flush?: boolean
}

/** `bg-surface border border-border rounded-lg p-4 md:p-6 shadow-sm` (05-frontend.md section 9.2). */
export function Card({ flush = false, className, ...props }: CardProps) {
  return (
    <div
      className={cn(
        'min-w-0 rounded-lg border border-border bg-surface text-text shadow-card',
        !flush && 'p-4 md:p-6',
        className,
      )}
      {...props}
    />
  )
}

export interface CardHeaderProps extends Omit<ComponentProps<'div'>, 'title'> {
  /** Right-aligned actions next to the title (a link such as "All results"). */
  actions?: ReactNode
}

export function CardHeader({ actions, className, children, ...props }: CardHeaderProps) {
  return (
    <div
      className={cn('mb-4 flex flex-wrap items-start justify-between gap-x-4 gap-y-2', className)}
      {...props}
    >
      <div className="flex min-w-0 flex-col gap-1">{children}</div>
      {actions && <div className="flex shrink-0 flex-wrap items-center gap-2">{actions}</div>}
    </div>
  )
}

export interface CardTitleProps extends ComponentProps<'h2'> {
  /** Follow the page outline; the look stays the same. */
  as?: 'h1' | 'h2' | 'h3' | 'h4'
}

/** Card titles are `text-lg` (18 px). */
export function CardTitle({ as: Tag = 'h2', className, ...props }: CardTitleProps) {
  return (
    <Tag
      className={cn('text-lg leading-snug font-semibold tracking-tight text-text', className)}
      {...props}
    />
  )
}

export function CardDescription({ className, ...props }: ComponentProps<'p'>) {
  return <p className={cn('text-sm text-muted', className)} {...props} />
}

export function CardContent({ className, ...props }: ComponentProps<'div'>) {
  return <div className={cn('min-w-0', className)} {...props} />
}

export function CardFooter({ className, ...props }: ComponentProps<'div'>) {
  return (
    <div
      className={cn(
        'mt-4 flex flex-wrap items-center gap-3 border-t border-border pt-4',
        className,
      )}
      {...props}
    />
  )
}
