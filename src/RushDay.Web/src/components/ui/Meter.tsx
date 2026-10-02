import type { ReactNode } from 'react'

import { cn } from '@/lib/cn'

export interface MeterProps {
  value: number
  max: number
  /** Accessible name ("Places filled on CS3099"). */
  label: string
  /** The number in words, always visible next to the bar ("12 of 30 places left"). */
  valueText: string
  /** Show the label above the bar; otherwise only assistive technology reads it. */
  showLabel?: boolean
  /** Tone by fill ratio: warning from `warningAt`, danger from `dangerAt` (the pool meter uses 0.8/0.95). */
  warningAt?: number
  dangerAt?: number
  /** Force a tone instead of deriving it from the ratio. */
  tone?: 'primary' | 'warning' | 'danger' | 'muted'
  size?: 'sm' | 'md' | 'lg'
  /** A text label for the warning/danger state so colour is never the only signal. */
  statusText?: ReactNode
  className?: string
}

const fill: Record<NonNullable<MeterProps['tone']>, string> = {
  primary: 'bg-primary',
  warning: 'bg-warning',
  danger: 'bg-danger',
  muted: 'bg-subtle',
}

const track: Record<NonNullable<MeterProps['tone']>, string> = {
  primary: 'bg-primary-soft',
  warning: 'bg-warning-soft',
  danger: 'bg-danger-soft',
  muted: 'bg-surface-2',
}

const heights = { sm: 'h-1.5', md: 'h-2', lg: 'h-3' } as const

/**
 * A ratio bar with `role="meter"` (capacity, the database pool, a credit budget). The value is
 * always also given as text, so the bar is never the only way to read it.
 */
export function Meter({
  value,
  max,
  label,
  valueText,
  showLabel = false,
  warningAt,
  dangerAt,
  tone,
  size = 'md',
  statusText,
  className,
}: MeterProps) {
  const safeMax = max > 0 ? max : 1
  const ratio = Math.min(1, Math.max(0, value / safeMax))
  const derived: NonNullable<MeterProps['tone']> =
    tone ??
    (dangerAt !== undefined && ratio >= dangerAt
      ? 'danger'
      : warningAt !== undefined && ratio >= warningAt
        ? 'warning'
        : 'primary')

  return (
    <div className={cn('flex min-w-0 flex-col gap-1.5', className)}>
      <div className="flex items-baseline justify-between gap-3 text-sm">
        {showLabel && <span className="font-medium text-text">{label}</span>}
        <span className={cn('tabular-nums text-muted', !showLabel && 'ml-0')}>{valueText}</span>
        {statusText && (
          <span
            className={cn(
              'ml-auto text-xs font-medium',
              derived === 'danger' ? 'text-danger' : 'text-warning',
            )}
          >
            {statusText}
          </span>
        )}
      </div>
      <div
        role="meter"
        aria-label={label}
        aria-valuemin={0}
        aria-valuemax={max}
        aria-valuenow={Math.min(Math.max(value, 0), max)}
        aria-valuetext={valueText}
        className={cn('w-full overflow-hidden rounded-full', heights[size], track[derived])}
      >
        <div
          className={cn('h-full rounded-full transition-[width] duration-300', fill[derived])}
          style={{ width: `${ratio * 100}%` }}
        />
      </div>
    </div>
  )
}
