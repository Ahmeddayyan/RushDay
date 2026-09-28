import type { ComponentProps } from 'react'

import { cn } from '@/lib/cn'

export function Card({ className, ...props }: ComponentProps<'div'>) {
  return (
    <div
      className={cn('rounded-lg border border-border bg-surface text-text shadow-xs', className)}
      {...props}
    />
  )
}

export function CardHeader({ className, ...props }: ComponentProps<'div'>) {
  return <div className={cn('flex flex-col gap-1 p-5 pb-0', className)} {...props} />
}

export interface CardTitleProps extends ComponentProps<'h2'> {
  /** Heading level should follow the page outline; the look stays the same. 'h1' is for a page
   *  whose only heading is inside this card, such as /login. */
  as?: 'h1' | 'h2' | 'h3' | 'h4'
}

export function CardTitle({ as: Tag = 'h2', className, ...props }: CardTitleProps) {
  return <Tag className={cn('text-base font-semibold tracking-tight', className)} {...props} />
}

export function CardDescription({ className, ...props }: ComponentProps<'p'>) {
  return <p className={cn('text-sm text-muted', className)} {...props} />
}

export function CardContent({ className, ...props }: ComponentProps<'div'>) {
  return <div className={cn('p-5', className)} {...props} />
}

export function CardFooter({ className, ...props }: ComponentProps<'div'>) {
  return (
    <div
      className={cn(
        'flex flex-wrap items-center gap-3 border-t border-border px-5 py-4',
        className,
      )}
      {...props}
    />
  )
}
