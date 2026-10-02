import { LoadingRegion, Skeleton } from '@/components/ui'

/** Skeleton rows shaped like the table that is loading, inside a busy region that names it. */
export function TableSkeleton({ label, rows = 6 }: { label: string; rows?: number }) {
  return (
    <LoadingRegion label={label}>
      <div className="overflow-hidden rounded-lg border border-border bg-surface">
        <Skeleton className="h-10 rounded-none" />
        <div className="divide-y divide-border">
          {Array.from({ length: rows }, (_, index) => (
            <div key={index} className="flex items-center gap-4 px-4 py-3">
              <Skeleton className="h-4 w-24" />
              <Skeleton className="h-4 flex-1" />
              <Skeleton className="h-4 w-20" />
              <Skeleton className="h-4 w-16" />
            </div>
          ))}
        </div>
      </div>
    </LoadingRegion>
  )
}
