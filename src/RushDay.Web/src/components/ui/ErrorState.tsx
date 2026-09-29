import { useEffect, useState, type ReactNode } from 'react'
import { RefreshCw, SearchX, TriangleAlert, type LucideIcon } from 'lucide-react'

import { isApiError } from '@/api/client'
import { describeProblem, type ProblemContext } from '@/api/problem'
import { cn } from '@/lib/cn'

import { Button } from './Button'
import { EmptyState } from './EmptyState'
import { ForbiddenState } from './ForbiddenState'

export interface ErrorStateProps {
  /** The thrown error; copy comes from `describeProblem` (05-frontend.md section 6.4). */
  error?: unknown
  /** Placeholders for the copy ({code}, {leader}, ...). */
  context?: ProblemContext
  /** Overrides the derived heading. */
  title?: string
  /** Overrides the derived sentence. */
  description?: ReactNode
  icon?: LucideIcon
  onRetry?: () => void
  retryLabel?: string
  action?: ReactNode
  compact?: boolean
  headingLevel?: 'h1' | 'h2' | 'h3' | 'p'
  className?: string
}

let nextErrorKey = 0
const errorKeys = new WeakMap<object, number>()

function keyOf(error: unknown): number {
  if (typeof error !== 'object' || error === null) return -1
  let key = errorKeys.get(error)
  if (key === undefined) {
    key = nextErrorKey++
    errorKeys.set(error, key)
  }
  return key
}

/** Retry, disabled with a visible count while the server's Retry-After runs. */
function RetryButton({
  seconds,
  label,
  onRetry,
}: {
  seconds: number
  label: string
  onRetry: () => void
}) {
  const [left, setLeft] = useState(seconds)

  useEffect(() => {
    if (left <= 0) return
    const timer = setTimeout(() => setLeft((value) => value - 1), 1000)
    return () => clearTimeout(timer)
  }, [left])

  return (
    <Button variant="secondary" size="sm" onClick={onRetry} disabled={left > 0}>
      <RefreshCw aria-hidden="true" className="size-4" />
      {left > 0 ? `${label} in ${left}s` : label}
    </Button>
  )
}

/**
 * A failed load: icon, one-sentence heading, the `describeProblem` sentence and Retry. A 403 renders
 * <ForbiddenState> and a 404 the "doesn't exist" <EmptyState>, so pages can pass any error here.
 */
export function ErrorState({
  error,
  context,
  title,
  description,
  icon: Icon = TriangleAlert,
  onRetry,
  retryLabel = 'Try again',
  action,
  compact = false,
  headingLevel: Heading = 'h2',
  className,
}: ErrorStateProps) {
  if (isApiError(error) && error.status === 403 && !title) {
    const copy = describeProblem(error, context)
    return (
      <ForbiddenState
        description={copy.message}
        compact={compact}
        headingLevel={Heading}
        {...(className ? { className } : {})}
        {...(action ? { action } : {})}
      />
    )
  }

  if (isApiError(error) && error.status === 404 && !title) {
    return (
      <EmptyState
        icon={SearchX}
        title={describeProblem(error, context).message}
        compact={compact}
        headingLevel={Heading}
        {...(className ? { className } : {})}
        {...(action ? { action } : {})}
      />
    )
  }

  const copy = error === undefined ? undefined : describeProblem(error, context)
  const heading = title ?? copy?.title ?? 'Something went wrong'
  const sentence = description ?? copy?.message
  const wait = copy?.action === 'wait' ? (copy.retryAfterSeconds ?? 0) : 0

  return (
    <div
      role="alert"
      className={cn(
        'flex flex-col items-center justify-center text-center',
        compact ? 'gap-2 px-4 py-8' : 'gap-3 px-6 py-14',
        className,
      )}
    >
      <span
        aria-hidden="true"
        className={cn(
          'flex items-center justify-center rounded-full bg-danger-soft text-danger',
          compact ? 'size-10' : 'size-12',
        )}
      >
        <Icon className={compact ? 'size-5' : 'size-6'} />
      </span>
      <Heading className={cn('font-semibold text-text', compact ? 'text-sm' : 'text-base')}>
        {heading}
      </Heading>
      {sentence && <div className="max-w-prose text-sm text-muted">{sentence}</div>}
      {(onRetry || action) && (
        <div className="mt-1 flex flex-wrap justify-center gap-2">
          {onRetry && (
            <RetryButton key={keyOf(error)} seconds={wait} label={retryLabel} onRetry={onRetry} />
          )}
          {action}
        </div>
      )}
    </div>
  )
}
