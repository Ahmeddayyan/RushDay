import type { ComponentProps } from 'react'
import type { LucideIcon } from 'lucide-react'

import { cn } from '@/lib/cn'

export type BadgeVariant = 'neutral' | 'info' | 'success' | 'warning' | 'danger'

/**
 * Soft background + text (+ icon): every pair is one of the 4.5:1 pairs that lib/contrast.test.ts
 * checks in both themes (`--success` on `--success-soft`, ...).
 */
const variants: Record<BadgeVariant, string> = {
  neutral: 'bg-surface-2 text-muted border-border',
  info: 'bg-info-soft text-info border-info/25',
  success: 'bg-success-soft text-success border-success/25',
  warning: 'bg-warning-soft text-warning border-warning/25',
  danger: 'bg-danger-soft text-danger border-danger/25',
}

export interface BadgeProps extends ComponentProps<'span'> {
  variant?: BadgeVariant
  /** Status badges carry an icon so colour is never the only signal (05-frontend.md section 9.3). */
  icon?: LucideIcon
}

export function Badge({
  variant = 'neutral',
  icon: Icon,
  className,
  children,
  ...props
}: BadgeProps) {
  return (
    <span
      className={cn(
        'inline-flex max-w-full items-center gap-1 rounded-full border px-2 py-0.5 text-xs leading-5 font-medium whitespace-nowrap',
        variants[variant],
        className,
      )}
      {...props}
    >
      {Icon && <Icon aria-hidden="true" className="size-3.5 shrink-0" />}
      <span className="truncate">{children}</span>
    </span>
  )
}
