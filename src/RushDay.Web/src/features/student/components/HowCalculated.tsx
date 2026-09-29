import { ChevronRight } from 'lucide-react'

import { cn } from '@/lib/cn'

/**
 * "How this is calculated" (05-frontend.md section 10), matching `Classification.cs`: a
 * credit-weighted average of published marks, absences and deferrals left out, the band thresholds,
 * and the exam board's say on the degree.
 */
export function HowCalculated({ className }: { className?: string }) {
  return (
    <details className={cn('group text-sm', className)}>
      <summary className="inline-flex cursor-pointer list-none items-center gap-1 rounded-sm font-medium text-primary hover:underline [&::-webkit-details-marker]:hidden">
        <ChevronRight
          aria-hidden="true"
          className="size-4 transition-transform group-open:rotate-90"
        />
        How this is calculated
      </summary>
      <div className="mt-2 space-y-2 border-l-2 border-border pl-3 text-muted">
        <p>
          Each published mark counts in proportion to the module&apos;s credits, so a 30-credit
          module counts twice as much as a 15-credit one. Absences and deferrals are left out.
        </p>
        <p>
          Bands: 70 and above First, 60 to 69 Upper Second (2:1), 50 to 59 Lower Second (2:2), 40 to
          49 Third, below 40 Fail.
        </p>
        <p>Your degree classification is decided by the exam board on your whole programme.</p>
      </div>
    </details>
  )
}
