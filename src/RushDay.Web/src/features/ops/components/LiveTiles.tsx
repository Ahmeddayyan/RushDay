import type { OpsSnapshot } from '@/api/types/ops'
import { formatDecimal, formatNumber } from '@/lib/format'

interface TileSpec {
  key: string
  label: string
  caption: string
  description: string
  read: (snapshot: OpsSnapshot) => number
  format: (value: number) => string
}

const TILES: TileSpec[] = [
  {
    key: 'requests',
    label: 'Visits per second',
    caption: 'requests/s',
    description: 'How many pages or actions the server answered each second, over the last minute.',
    read: (s) => s.http.last60s.perSecond,
    format: (v) => formatDecimal(v, 1),
  },
  {
    key: 'inFlight',
    label: 'Pages in progress',
    caption: 'in flight',
    description: 'Requests the server is working on right now.',
    read: (s) => s.http.inFlight,
    format: (v) => formatNumber(Math.round(v)),
  },
  {
    key: 'p95',
    label: 'Slowest 5% of requests',
    caption: 'p95 ms',
    description: '19 of 20 requests were faster than this, over the last minute.',
    read: (s) => s.http.last60s.p95Ms,
    format: (v) => `${formatNumber(Math.round(v))} ms`,
  },
  {
    key: 'p99',
    label: 'Slowest 1 in 100',
    caption: 'p99 ms',
    description: '99 of 100 requests were faster than this, over the last minute.',
    read: (s) => s.http.last60s.p99Ms,
    format: (v) => `${formatNumber(Math.round(v))} ms`,
  },
  {
    key: 'status5xx',
    label: 'Server errors',
    caption: '5xx',
    description: 'Requests that failed because of a real server error, over the last minute.',
    read: (s) => s.http.last60s.status5xx,
    format: (v) => formatNumber(Math.round(v)),
  },
  {
    key: 'rateLimited429',
    label: 'Asked to slow down',
    caption: '429',
    description: 'A visitor sent requests faster than their limit allows, over the last minute.',
    read: (s) => s.http.last60s.rateLimited429,
    format: (v) => formatNumber(Math.round(v)),
  },
  {
    key: 'shed503',
    label: 'Turned away while busy (fast try-again)',
    caption: '503 shed',
    description:
      'The server was at capacity and asked the visitor to try again immediately, over the last minute.',
    read: (s) => s.http.last60s.shed503,
    format: (v) => formatNumber(Math.round(v)),
  },
  {
    key: 'workingSet',
    label: 'Memory in use',
    caption: 'working set MB',
    description: "The server process's working set right now.",
    read: (s) => s.process.workingSetBytes / 1_000_000,
    format: (v) => `${formatDecimal(v, 0)} MB`,
  },
]

/** A tiny hand-drawn polyline: decorative only (`aria-hidden`), so no charting library is needed. */
function Sparkline({ values }: { values: number[] }) {
  if (values.length < 2) return null
  const width = 100
  const height = 28
  const min = Math.min(...values)
  const max = Math.max(...values)
  const span = max - min || 1
  const points = values
    .map((value, index) => {
      const x = (index / (values.length - 1)) * width
      const y = height - ((value - min) / span) * height
      return `${x.toFixed(1)},${y.toFixed(1)}`
    })
    .join(' ')

  return (
    <svg
      aria-hidden="true"
      viewBox={`0 0 ${width} ${height}`}
      preserveAspectRatio="none"
      className="h-7 w-full text-primary"
    >
      <polyline points={points} fill="none" stroke="currentColor" strokeWidth={1.5} />
    </svg>
  )
}

export interface LiveTilesProps {
  /** Up to 60 samples, oldest first (client-side history; the server's own `series` covers 60 minutes). */
  history: OpsSnapshot[]
  stale?: boolean
}

/** The eight live sparkline tiles of `/admin/ops` (05-frontend.md section 10). */
export function LiveTiles({ history, stale = false }: LiveTilesProps) {
  if (history.length === 0) return null
  const latest = history[history.length - 1]
  if (!latest) return null

  return (
    <div
      className="grid grid-cols-2 gap-3 sm:grid-cols-3 lg:grid-cols-4"
      aria-busy={stale || undefined}
    >
      {TILES.map((tile) => {
        const values = history.map(tile.read)
        const current = tile.read(latest)
        const min = Math.min(...values)
        const max = Math.max(...values)
        return (
          <div
            key={tile.key}
            className="flex min-w-0 flex-col gap-1 rounded-lg border border-border bg-surface p-3 shadow-card transition-opacity"
            style={stale ? { opacity: 0.6 } : undefined}
          >
            <p className="text-xs font-medium text-muted">{tile.label}</p>
            <p className="text-xl leading-tight font-semibold tracking-tight text-text tabular-nums">
              {tile.format(current)}
            </p>
            <p className="font-mono text-[11px] text-subtle">{tile.caption}</p>
            <Sparkline values={values} />
            <p className="text-[11px] text-muted">
              {values.length === 1
                ? 'The trend fills in every 5 seconds.'
                : `Last ${values.length} samples: ${tile.format(min)}–${tile.format(max)}`}
            </p>
            <span className="sr-only">{tile.description}</span>
          </div>
        )
      })}
    </div>
  )
}
