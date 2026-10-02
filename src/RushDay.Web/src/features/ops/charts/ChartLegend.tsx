import type { CSSProperties } from 'react'

import { cn } from '@/lib/cn'

export interface ChartLegendItem {
  label: string
  color: string
  /** `bar` draws a filled square, `line` a short line with a ringed marker (matching the chart). */
  shape: 'bar' | 'line'
  /** The series has no data yet: drawn dashed, with the note after the label. */
  missing?: string
}

/**
 * The legend of every multi-series chart (05-frontend.md section 11), rendered as HTML rather than
 * Recharts' `<Legend>`: Recharts sorts legend entries alphabetically and colours their text with the
 * series colour, so the legend read in a different order from the bars and some labels fell below
 * text contrast. Here the order is the caller's (the order of the marks), the text is in text
 * tokens, and the swatch alone carries the colour. It is `aria-hidden`: every chart has a table twin
 * and a summary sentence, so the legend is a visual key only.
 */
export function ChartLegend({
  items,
  className,
}: {
  items: ChartLegendItem[]
  className?: string
}) {
  return (
    <ul
      aria-hidden="true"
      className={cn('flex flex-wrap items-center gap-x-5 gap-y-1.5 text-xs text-muted', className)}
    >
      {items.map((item) => (
        <li key={item.label} className="inline-flex items-center gap-2">
          {item.shape === 'bar' ? (
            <span
              className={cn(
                'size-3 shrink-0 rounded-[3px]',
                item.missing && 'border border-dashed',
              )}
              style={
                (item.missing
                  ? { borderColor: item.color }
                  : { backgroundColor: item.color }) as CSSProperties
              }
            />
          ) : (
            <svg width="22" height="10" viewBox="0 0 22 10" className="shrink-0">
              <line
                x1="1"
                x2="21"
                y1="5"
                y2="5"
                stroke={item.color}
                strokeWidth="2"
                strokeDasharray={item.missing ? '3 3' : undefined}
              />
              {!item.missing && (
                <circle
                  cx="11"
                  cy="5"
                  r="3.5"
                  fill="var(--surface)"
                  stroke={item.color}
                  strokeWidth="2"
                />
              )}
            </svg>
          )}
          <span className="text-text">{item.label}</span>
          {item.missing && <span className="italic">{item.missing}</span>}
        </li>
      ))}
    </ul>
  )
}
