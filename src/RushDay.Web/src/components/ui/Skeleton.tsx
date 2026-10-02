import type { ComponentProps } from 'react'

import { cn } from '@/lib/cn'

/**
 * A placeholder shape matching the final layout (a line of text, a tile, a table row). Always
 * `aria-hidden`: the surrounding <LoadingRegion> says what is loading.
 */
export function Skeleton({ className, ...props }: ComponentProps<'div'>) {
  return (
    <div
      aria-hidden="true"
      className={cn('animate-pulse rounded-md bg-border/70', className)}
      {...props}
    />
  )
}

/** A few lines of text-height skeletons. */
export function SkeletonText({ lines = 3, className }: { lines?: number; className?: string }) {
  return (
    <div aria-hidden="true" className={cn('space-y-2.5', className)}>
      {Array.from({ length: lines }, (_, index) => (
        <Skeleton key={index} className={cn('h-4', index === lines - 1 ? 'w-2/3' : 'w-full')} />
      ))}
    </div>
  )
}
