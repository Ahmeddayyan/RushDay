import { cn } from '@/lib/cn'
import { formatDateTime } from '@/lib/format'
import { aboutSentence, useCountdown } from '@/lib/useCountdown'

export interface CountdownProps {
  /** The instant (ISO-8601 UTC) being counted down to. */
  target: string
  /** The institution's zone, for the static sentence. */
  timeZone: string
  /** Server clock minus browser clock (`useServerClock().offsetMs`). */
  serverOffsetMs?: number
  /** Start of the static sentence: "Results publish at" → "Results publish at 28 September 2026 at 09:00 (BST)". */
  sentencePrefix?: string
  /** Runs once, 0–30 s after the instant passes while mounted (see lib/useCountdown). */
  onElapsed?: () => void
  /** The status sentence once the instant has passed. */
  elapsedText?: string
  variant?: 'blocks' | 'inline'
  className?: string
}

const pad = (value: number) => String(value).padStart(2, '0')

/**
 * A countdown (05-frontend.md section 9.3): the ticking digits are `aria-hidden`; a visually hidden
 * static sentence always states the absolute instant and zone; one `role="status"` sentence says
 * roughly how long is left and changes at most once per minute, so a screen reader is not flooded.
 */
export function Countdown({
  target,
  timeZone,
  serverOffsetMs = 0,
  sentencePrefix = 'Due at',
  onElapsed,
  elapsedText,
  variant = 'blocks',
  className,
}: CountdownProps) {
  const state = useCountdown(target, { serverOffsetMs, ...(onElapsed ? { onElapsed } : {}) })
  if (!state) return null

  const segments: [number, string, string][] = [
    ...(state.days > 0 ? ([[state.days, 'days', 'd']] as [number, string, string][]) : []),
    [state.hours, 'hours', 'h'],
    [state.minutes, 'min', 'm'],
    [state.seconds, 'sec', 's'],
  ]

  return (
    <div className={cn('min-w-0', className)}>
      <p className="sr-only">
        {sentencePrefix} {formatDateTime(target, timeZone)}.
      </p>
      {variant === 'blocks' ? (
        <div aria-hidden="true" className="flex flex-wrap gap-2">
          {segments.map(([value, unit]) => (
            <div
              key={unit}
              className="flex min-w-14 flex-col items-center rounded-md border border-border bg-surface-2 px-2.5 py-1.5"
            >
              <span className="text-xl leading-tight font-semibold text-text tabular-nums">
                {unit === 'days' ? value : pad(value)}
              </span>
              <span className="text-xs text-muted">{unit}</span>
            </div>
          ))}
        </div>
      ) : (
        <span aria-hidden="true" className="font-medium text-text tabular-nums">
          {state.days > 0 ? `${state.days}d ` : ''}
          {pad(state.hours)}:{pad(state.minutes)}:{pad(state.seconds)}
        </span>
      )}
      <p role="status" className="sr-only">
        {aboutSentence(state.remainingMs, elapsedText)}
      </p>
    </div>
  )
}
