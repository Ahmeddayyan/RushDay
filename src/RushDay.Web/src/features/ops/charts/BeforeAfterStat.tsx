import type { ReactNode } from 'react'

export interface BeforeAfterStatProps {
  /** The plain-words name, primary. */
  label: string
  /** The technical metric name, shown in font-mono (05-frontend.md section 9.3, StatTile pattern). */
  caption?: string
  before: ReactNode
  after: ReactNode
  detail?: ReactNode
}

/** One headline number of a KPI row (`ResultsDayTiles`, `LoginStormTiles`), shown as before → after. */
export function BeforeAfterStat({ label, caption, before, after, detail }: BeforeAfterStatProps) {
  return (
    <div className="flex min-w-0 flex-col gap-1 rounded-lg border border-border bg-surface p-4 shadow-card">
      <p className="text-sm font-medium text-muted">{label}</p>
      <div className="flex flex-wrap items-baseline gap-2 tabular-nums">
        <span className="text-base font-medium text-muted">{before}</span>
        <span aria-hidden="true" className="text-muted">
          →
        </span>
        <span className="text-2xl leading-tight font-semibold tracking-tight text-text">
          {after}
        </span>
      </div>
      {caption && <p className="font-mono text-xs text-subtle">{caption}</p>}
      {detail && <div className="text-xs text-muted">{detail}</div>}
    </div>
  )
}
