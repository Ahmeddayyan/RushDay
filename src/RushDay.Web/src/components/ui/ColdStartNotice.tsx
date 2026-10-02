import { Hourglass } from 'lucide-react'

import { cn } from '@/lib/cn'

/**
 * Shown after 3 s of waiting for the server (05-frontend.md section 5.1): the free hosting tier
 * spins the container down when idle, and the first request after that takes up to a minute.
 */
export function ColdStartNotice({ className }: { className?: string }) {
  return (
    <div
      role="status"
      className={cn(
        'flex items-start gap-3 rounded-md border border-info/25 bg-info-soft px-3.5 py-3 text-sm text-info',
        className,
      )}
    >
      <Hourglass aria-hidden="true" className="mt-0.5 size-4 shrink-0 motion-safe:animate-pulse" />
      <p>Waking the server. On the free tier this can take up to a minute.</p>
    </div>
  )
}
