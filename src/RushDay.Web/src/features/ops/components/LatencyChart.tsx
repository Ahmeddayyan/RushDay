import {
  CartesianGrid,
  Legend,
  Line,
  LineChart,
  ResponsiveContainer,
  Tooltip,
  XAxis,
  YAxis,
} from 'recharts'

import type { OpsSeriesPoint } from '@/api/types/ops'
import { Table, TableBody, TableCell, TableHead, TableHeaderCell, TableRow } from '@/components/ui'
import { useChartPalette } from '@/features/ops/charts/palette'
import { useReducedMotion } from '@/features/ops/useReducedMotion'
import { formatNumber, formatTime } from '@/lib/format'

export interface LatencyChartProps {
  series: OpsSeriesPoint[]
}

/**
 * The last 60 minutes of latency (05-frontend.md section 11): slowest 5% (p95) and slowest 1 in 100
 * (p99) as ordinal steps with a legend and end labels. `OpsSnapshot.series`
 * (04-performance-and-ops.md section 6.3) carries only `p95Ms`/`p99Ms` per minute, not a per-minute
 * p50 — the current typical response time is in the health summary line above instead, so this chart
 * shows the two series the data actually has rather than a fabricated third one (spec gap, see PR
 * description).
 */
export function LatencyChart({ series }: LatencyChartProps) {
  const palette = useChartPalette()
  const reducedMotion = useReducedMotion()
  const hasData = series.some((point) => point.requests > 0)

  if (!hasData) {
    return (
      <p className="py-8 text-center text-sm text-muted">Collecting the first minute of data…</p>
    )
  }

  const data = series.map((point) => ({ ...point, label: formatTime(point.minute) }))

  return (
    <div>
      <div style={{ width: '100%', height: 220 }}>
        <ResponsiveContainer>
          <LineChart data={data} margin={{ top: 8, right: 16, left: 0, bottom: 8 }}>
            <CartesianGrid stroke={palette.grid} />
            <XAxis dataKey="label" stroke={palette.muted} fontSize={12} minTickGap={24} />
            <YAxis stroke={palette.muted} fontSize={12} unit="ms" />
            <Tooltip
              labelFormatter={(label) => (typeof label === 'string' ? label : '')}
              formatter={(value) => `${formatNumber(Number(value))} ms`}
            />
            <Legend />
            <Line
              type="monotone"
              dataKey="p95Ms"
              name="Slowest 5% (p95)"
              stroke={palette.accent}
              strokeWidth={2}
              dot={false}
              isAnimationActive={!reducedMotion}
            />
            <Line
              type="monotone"
              dataKey="p99Ms"
              name="Slowest 1 in 100 (p99)"
              stroke={palette.accent3}
              strokeWidth={2}
              dot={false}
              isAnimationActive={!reducedMotion}
            />
          </LineChart>
        </ResponsiveContainer>
      </div>
      <details className="mt-2">
        <summary className="cursor-pointer text-sm text-muted">View as table</summary>
        <Table caption="Latency per minute, last 60 minutes" captionHidden className="mt-2">
          <TableHead>
            <TableRow>
              <TableHeaderCell>Minute</TableHeaderCell>
              <TableHeaderCell numeric>Slowest 5% (ms)</TableHeaderCell>
              <TableHeaderCell numeric>Slowest 1 in 100 (ms)</TableHeaderCell>
            </TableRow>
          </TableHead>
          <TableBody>
            {series.map((point) => (
              <TableRow key={point.minute}>
                <TableCell label="Minute">{formatTime(point.minute)}</TableCell>
                <TableCell numeric label="Slowest 5% (ms)">
                  {formatNumber(Math.round(point.p95Ms))}
                </TableCell>
                <TableCell numeric label="Slowest 1 in 100 (ms)">
                  {formatNumber(Math.round(point.p99Ms))}
                </TableCell>
              </TableRow>
            ))}
          </TableBody>
        </Table>
      </details>
    </div>
  )
}
