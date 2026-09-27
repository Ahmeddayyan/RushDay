import type { ComponentProps } from 'react'

import { cn } from '@/lib/cn'

export type BadgeVariant = 'neutral' | 'primary' | 'success' | 'warning' | 'danger'

const variants: Record<BadgeVariant, string> = {
  neutral: 'bg-border/60 text-text',
  primary: 'bg-primary/12 text-primary',
  success: 'bg-success/12 text-success',
  warning: 'bg-warning/15 text-warning',
  danger: 'bg-danger/12 text-danger',
}

export interface BadgeProps extends ComponentProps<'span'> {
  variant?: BadgeVariant
}

export function Badge({ variant = 'neutral', className, ...props }: BadgeProps) {
  return (
    <span
      className={cn(
        'inline-flex items-center gap-1 rounded-full px-2.5 py-0.5 text-xs font-semibold whitespace-nowrap',
        variants[variant],
        className,
      )}
      {...props}
    />
  )
}
