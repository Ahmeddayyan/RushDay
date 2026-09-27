import type { ReactNode } from 'react'
import { Inbox, type LucideIcon } from 'lucide-react'

import { cn } from '@/lib/cn'

export interface EmptyStateProps {
  title: string
  description?: string
  icon?: LucideIcon
  /** Usually a <Button> or <ButtonLink>. */
  action?: ReactNode
  /** Tighter spacing for use inside a card. */
  compact?: boolean
  className?: string
}

export function EmptyState({
  title,
  description,
  icon: Icon = Inbox,
  action,
  compact = false,
  className,
}: EmptyStateProps) {
  return (
    <div
      className={cn(
        'flex flex-col items-center justify-center text-center',
        compact ? 'gap-2 px-4 py-8' : 'gap-3 px-6 py-16',
        className,
      )}
    >
      <span
        aria-hidden="true"
        className={cn(
          'flex items-center justify-center rounded-full bg-border/50 text-muted',
          compact ? 'size-10' : 'size-14',
        )}
      >
        <Icon className={compact ? 'size-5' : 'size-7'} />
      </span>
      <p className={cn('font-semibold text-text', compact ? 'text-sm' : 'text-base')}>{title}</p>
      {description && <p className="max-w-prose text-sm text-muted">{description}</p>}
      {action && <div className="mt-2">{action}</div>}
    </div>
  )
}
