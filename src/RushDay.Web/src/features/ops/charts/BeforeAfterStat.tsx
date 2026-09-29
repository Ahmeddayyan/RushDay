import type { ReactNode } from 'react'

export interface BeforeAfterStatProps {
  /** The plain-words name, primary. */
  label: string
  /** The technical metric name, shown in font-mono (05-frontend.md section 9.3, StatTile pattern). */
  caption?: string
  /** The "before" (v0) figure, formatted; undefined when that run did not record it. */
  before: string | undefined
  /** The "after" (v1) figure, formatted; undefined until a v1 run is committed. */
  after: string | undefined
  detail?: ReactNode
}

function statusLine(before: string | undefined, after: string | undefined): string {
  if (before !== undefined && after !== undefined) return 'Before (v0) → after (v1)'
  if (before !== undefined) return 'Before (v0). After (v1): not yet measured.'
  if (after !== undefined) return 'After (v1). Not recorded before.'
  return 'Not recorded in the v0 run. After (v1): not yet measured.'
}

/**
 * One headline number of a KPI row (`ResultsDayTiles`), shown as before → after. Only measured
 * figures are set large: a missing side is named in the small line under the number rather than
 * printed as a headline, so "not yet measured" never crowds a tile. The visible parts are hidden
 * from assistive technology in favour of one plain sentence.
 */
export function BeforeAfterStat({ label, caption, before, after, detail }: BeforeAfterStatProps) {
  const sentence = `Before (v0): ${before ?? 'not recorded'}. After (v1): ${after ?? 'not yet measured'}.`
  const value = 'text-2xl leading-8 font-semibold tracking-tight text-text'

  return (
    <div className="flex min-w-0 flex-col rounded-lg border border-border bg-surface p-4 shadow-card">
      {/* Two lines tall at most widths, so the figures of a row of tiles line up. */}
      <p className="text-sm leading-5 font-medium text-muted sm:min-h-10">{label}</p>
      <p className="sr-only">{sentence}</p>
      <div aria-hidden="true" className="mt-2">
        <p className="flex flex-wrap items-baseline gap-x-2 tabular-nums">
          {before !== undefined && after !== undefined ? (
            <>
              <span className="text-base font-medium text-muted">{before}</span>
              <span className="text-muted">→</span>
              <span className={value}>{after}</span>
            </>
          ) : before !== undefined || after !== undefined ? (
            <span className={value}>{before ?? after}</span>
          ) : (
            <span className="text-base leading-8 font-medium text-muted">Not recorded</span>
          )}
        </p>
        <p className="mt-0.5 text-xs text-muted">{statusLine(before, after)}</p>
      </div>
      {(caption || detail) && (
        <div className="mt-auto space-y-0.5 pt-3">
          {caption && (
            <p className="font-mono text-xs wrap-anywhere text-subtle">
              {/* A break opportunity after each dot, so a dotted metric name wraps between words. */}
              {caption.replaceAll('.', '.' + String.fromCharCode(0x200b))}
            </p>
          )}
          {detail && <p className="text-xs text-muted">{detail}</p>}
        </div>
      )}
    </div>
  )
}
