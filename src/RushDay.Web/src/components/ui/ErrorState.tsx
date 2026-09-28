import type { ReactNode } from 'react'
import { RefreshCw, TriangleAlert, type LucideIcon } from 'lucide-react'

import { describeProblem } from '@/api/problem'
import { cn } from '@/lib/cn'

import { Button } from './Button'

export interface ErrorStateProps {
  title?: string
  /** Shown as-is. When omitted, a message is derived from `error`. */
  description?: string
  error?: unknown
  icon?: LucideIcon
  onRetry?: () => void
  retryLabel?: string
  action?: ReactNode
  compact?: boolean
  className?: string
}

export function ErrorState({
  title = 'Something went wrong',
  description,
  error,
  icon: Icon = TriangleAlert,
  onRetry,
  retryLabel = 'Try again',
  action,
  compact = false,
  className,
}: ErrorStateProps) {
  const message = description ?? (error === undefined ? undefined : describeProblem(error).message)

  return (
    <div
      role="alert"
      className={cn(
        'flex flex-col items-center justify-center text-center',
        compact ? 'gap-2 px-4 py-8' : 'gap-3 px-6 py-16',
        className,
      )}
    >
      <span
        aria-hidden="true"
        className={cn(
          'flex items-center justify-center rounded-full bg-danger/12 text-danger',
          compact ? 'size-10' : 'size-14',
        )}
      >
        <Icon className={compact ? 'size-5' : 'size-7'} />
      </span>
      <p className={cn('font-semibold text-text', compact ? 'text-sm' : 'text-base')}>{title}</p>
      {message && <p className="max-w-prose text-sm text-muted">{message}</p>}
      {(onRetry || action) && (
        <div className="mt-2 flex flex-wrap justify-center gap-2">
          {onRetry && (
            <Button variant="secondary" size="sm" onClick={onRetry}>
              <RefreshCw aria-hidden="true" className="size-4" />
              {retryLabel}
            </Button>
          )}
          {action}
        </div>
      )}
    </div>
  )
}
