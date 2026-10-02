import { useId, type ReactNode } from 'react'
import { Info, type LucideIcon } from 'lucide-react'

import { cn } from '@/lib/cn'

import { Tooltip } from './Tooltip'

export interface StatTileProps {
  /** The plain-words name ("Visits per second"), primary. */
  label: string
  value: ReactNode
  /** The technical metric name ("requests/s"), shown in font-mono. */
  caption?: string
  /** A longer explanation: a tooltip for sighted users and `aria-describedby` text for everyone. */
  description?: string
  /** Extra line under the value (min/max, a comparison). */
  detail?: ReactNode
  icon?: LucideIcon
  tone?: 'default' | 'warning' | 'danger' | 'success'
  /** Greys the tile when its data is stale (a failed poll keeps the last sample). */
  stale?: boolean
  className?: string
  children?: ReactNode
}

const toneClass: Record<NonNullable<StatTileProps['tone']>, string> = {
  default: 'text-text',
  success: 'text-success',
  warning: 'text-warning',
  danger: 'text-danger',
}

/** A headline number (05-frontend.md section 9.3). */
export function StatTile({
  label,
  value,
  caption,
  description,
  detail,
  icon: Icon,
  tone = 'default',
  stale = false,
  className,
  children,
}: StatTileProps) {
  const descriptionId = useId()
  const labelId = useId()

  return (
    <div
      role="group"
      aria-labelledby={labelId}
      aria-describedby={description ? descriptionId : undefined}
      className={cn(
        'flex min-w-0 flex-col gap-1 rounded-lg border border-border bg-surface p-4 shadow-card transition-opacity',
        stale && 'opacity-60',
        className,
      )}
    >
      <div className="flex items-start justify-between gap-2">
        <p className="flex min-w-0 items-center gap-2 text-sm font-medium text-muted">
          {Icon && <Icon aria-hidden="true" className="size-4 shrink-0" />}
          <span id={labelId}>{label}</span>
        </p>
        {description && (
          <Tooltip content={description}>
            <button
              type="button"
              aria-label={`About ${label}`}
              className="-m-1 flex size-6 shrink-0 cursor-help items-center justify-center rounded-full text-muted hover:text-text"
            >
              <Info aria-hidden="true" className="size-4" />
            </button>
          </Tooltip>
        )}
      </div>
      <p
        className={cn(
          'text-2xl leading-tight font-semibold tracking-tight tabular-nums',
          toneClass[tone],
        )}
      >
        {value}
      </p>
      {caption && <p className="font-mono text-xs text-subtle">{caption}</p>}
      {detail && <div className="text-xs text-muted">{detail}</div>}
      {children}
      {description && (
        <span id={descriptionId} className="sr-only">
          {description}
        </span>
      )}
    </div>
  )
}
