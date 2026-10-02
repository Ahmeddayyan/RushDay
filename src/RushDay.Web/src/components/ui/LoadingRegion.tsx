import type { ReactNode } from 'react'

import { cn } from '@/lib/cn'

export interface LoadingRegionProps {
  /** What is loading, as a noun phrase: "results" → "Loading results". */
  label: string
  children: ReactNode
  className?: string
}

/**
 * Wraps skeletons in a busy region with one visually hidden "Loading {page}" sentence
 * (05-frontend.md section 9.3), so a screen reader hears one message instead of silence.
 */
export function LoadingRegion({ label, children, className }: LoadingRegionProps) {
  return (
    <div aria-busy="true" className={cn('min-w-0', className)}>
      <p role="status" className="sr-only">
        Loading {label}
      </p>
      {children}
    </div>
  )
}

export interface RefetchingProps {
  /** True while a refetch runs over content that is already on screen. */
  active: boolean
  children: ReactNode
  className?: string
}

/** Keeps the old content at 60% opacity while it refreshes (05-frontend.md section 10 conventions). */
export function Refetching({ active, children, className }: RefetchingProps) {
  return (
    <div
      aria-busy={active || undefined}
      className={cn('min-w-0 transition-opacity', active && 'opacity-60', className)}
    >
      {children}
    </div>
  )
}
