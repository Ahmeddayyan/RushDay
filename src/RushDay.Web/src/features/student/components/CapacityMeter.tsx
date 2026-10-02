import type { ModuleSummary } from '@/api/types/common'
import { Meter } from '@/components/ui'
import { formatRelative } from '@/lib/format'

import { placesLeftText } from '../copy'

export interface CapacityMeterProps {
  module: Pick<ModuleSummary, 'code' | 'capacity' | 'enrolledCount' | 'placesRemaining'>
  /** When a 409 `module-full` answered this student's attempt: "Filled just now". */
  filledAt?: number | null
  size?: 'md' | 'lg'
  className?: string
}

/**
 * Places on a module (05-frontend.md section 10): the bar fills with enrolledCount / capacity and
 * the sentence "12 of 30 places left" is always shown, so the bar is never the only way to read it.
 * Few places left and Full are also said in words.
 */
export function CapacityMeter({ module, filledAt, size = 'md', className }: CapacityMeterProps) {
  const { code, capacity, enrolledCount, placesRemaining } = module
  const full = placesRemaining <= 0
  const few = !full && capacity > 0 && placesRemaining / capacity <= 0.2

  return (
    <div className={className}>
      <Meter
        label={`Places filled on ${code}`}
        value={Math.min(enrolledCount, capacity)}
        max={capacity}
        valueText={placesLeftText(Math.max(0, placesRemaining), capacity)}
        size={size}
        tone={full ? 'danger' : few ? 'warning' : 'primary'}
        {...(full ? { statusText: 'Full' } : few ? { statusText: 'Few places left' } : {})}
      />
      {filledAt !== null && filledAt !== undefined && (
        <p className="mt-1 text-xs text-muted">Filled {formatRelative(filledAt)}</p>
      )}
    </div>
  )
}
