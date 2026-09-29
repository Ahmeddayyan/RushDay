import {
  CartesianGrid,
  Line,
  LineChart,
  ResponsiveContainer,
  Tooltip,
  XAxis,
  YAxis,
} from 'recharts'

import type { OpsSeriesPoint } from '@/api/types/ops'
import { Table, TableBody, TableCell, TableHead, TableHeaderCell, TableRow } from '@/components/ui'
import { ChartLegend } from '@/features/ops/charts/ChartLegend'
import {
  axisTickStyle,
  chartTooltipStyle,
  formatAxisNumber,
} from '@/features/ops/charts/chartStyle'
import { useChartPalette } from '@/features/ops/charts/palette'
import { useReducedMotion } from '@/features/ops/useReducedMotion'
import { formatNumber, formatTime } from '@/lib/format'

import { sinceStart } from './seriesSinceStart'

export interface LatencyChartProps {
  series: OpsSeriesPoint[]
  /** The server's start: minutes before it are not history, so they are not drawn as zeros. */
  startedAt?: string
  /** When the sample was taken: the minute still in progress is left out until it is complete. */
  sampledAt?: string
  timeZone?: string
}

/**
 * The last 60 minutes of latency (05-frontend.md section 11): slowest 5% (p95) and slowest 1 in 100
 * (p99) as ordinal steps with a legend in that order. `OpsSnapshot.series`
 * (04-performance-and-ops.md section 6.3) carries only `p95Ms`/`p99Ms` per minute, not a per-minute
 * p50 — the current typical response time is in the health summary line above instead, so this chart
 * shows the two series the data actually has rather than a fabricated third one (spec gap, see PR
 * description).
 */
export function LatencyChart({ series, startedAt, sampledAt, timeZone }: LatencyChartProps) {
  const palette = useChartPalette()
  const reducedMotion = useReducedMotion()
  const points = sinceStart(series, startedAt, sampledAt)
  const hasData = points.some((point) => point.requests > 0)

  if (!hasData) {
    return (
      <p className="py-8 text-center text-sm text-muted">Collecting the first minute of data…</p>
    )
  }

  // A minute with no requests has no latency: a gap in the line, not a fall to 0 ms.
  const data = points.map((point) => ({
    ...point,
    p95Ms: point.requests > 0 ? point.p95Ms : null,
    p99Ms: point.requests > 0 ? point.p99Ms : null,
    label: formatTime(point.minute, timeZone),
  }))

  return (
    <div className="space-y-3">
      <ChartLegend
        items={[
          { label: 'Slowest 5% (p95)', color: palette.accent, shape: 'line' },
          { label: 'Slowest 1 in 100 (p99)', color: palette.accent3, shape: 'line' },
        ]}
      />
      <div style={{ width: '100%', height: 220 }}>
        <ResponsiveContainer>
          <LineChart data={data} margin={{ top: 8, right: 12, left: 0, bottom: 0 }}>
            <CartesianGrid stroke={palette.grid} vertical={false} />
            <XAxis
              dataKey="label"
              tick={axisTickStyle}
              tickLine={false}
              stroke={palette.grid}
              minTickGap={24}
            />
            <YAxis
              width={64}
              tick={axisTickStyle}
              tickLine={false}
              axisLine={false}
              tickFormatter={(value: number) => `${formatAxisNumber(value)} ms`}
            />
            <Tooltip
              {...chartTooltipStyle}
              cursor={{ stroke: 'var(--border-strong)' }}
              labelFormatter={(label) => (typeof label === 'string' ? label : '')}
              formatter={(value) => `${formatNumber(Math.round(Number(value)))} ms`}
            />
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
      <details>
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
            {points.map((point) => (
              <TableRow key={point.minute}>
                <TableCell label="Minute">{formatTime(point.minute, timeZone)}</TableCell>
                <TableCell numeric label="Slowest 5% (ms)">
                  {point.requests > 0 ? formatNumber(Math.round(point.p95Ms)) : 'no requests'}
                </TableCell>
                <TableCell numeric label="Slowest 1 in 100 (ms)">
                  {point.requests > 0 ? formatNumber(Math.round(point.p99Ms)) : 'no requests'}
                </TableCell>
              </TableRow>
            ))}
          </TableBody>
        </Table>
      </details>
    </div>
  )
}
