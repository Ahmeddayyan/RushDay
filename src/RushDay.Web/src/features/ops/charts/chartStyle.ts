/** Shared Recharts styling for the load-story and live charts (05-frontend.md section 11). */

/**
 * Recharts tooltip look in the app's tokens, so it reads in both themes (the default is a white box
 * with grey text, unreadable in dark mode).
 */
export const chartTooltipStyle = {
  contentStyle: {
    background: 'var(--surface)',
    border: '1px solid var(--border-strong)',
    borderRadius: 8,
    color: 'var(--text)',
    fontSize: 12,
    boxShadow: 'var(--shadow)',
  },
  labelStyle: { color: 'var(--text)', fontWeight: 600, marginBottom: 2 },
  itemStyle: { color: 'var(--text)', padding: 0 },
} as const

/** Axis ticks in the muted text token, 12 px, with no tick marks cluttering the hairline axis. */
export const axisTickStyle = { fill: 'var(--muted)', fontSize: 12 } as const

const compact = new Intl.NumberFormat('en-GB', { notation: 'compact', maximumFractionDigits: 1 })

/** 2500 → "2.5K", 100000 → "100K", 750 → "750": short enough for a narrow y-axis. */
export function formatAxisNumber(value: number): string {
  return Math.abs(value) >= 1000 ? compact.format(value) : String(value)
}
