import {
  Area,
  AreaChart,
  CartesianGrid,
  ResponsiveContainer,
  Tooltip,
  XAxis,
  YAxis,
} from 'recharts'

import type { OpsSeriesPoint } from '@/api/types/ops'
import { Table, TableBody, TableCell, TableHead, TableHeaderCell, TableRow } from '@/components/ui'
import {
  axisTickStyle,
  chartTooltipStyle,
  formatAxisNumber,
} from '@/features/ops/charts/chartStyle'
import { useChartPalette } from '@/features/ops/charts/palette'
import { useReducedMotion } from '@/features/ops/useReducedMotion'
import { formatNumber, formatTime } from '@/lib/format'

import { sinceStart } from './seriesSinceStart'

export interface RequestRateChartProps {
  series: OpsSeriesPoint[]
  /** The server's start: minutes before it are not history, so they are not drawn as zeros. */
  startedAt?: string
  /** When the sample was taken: the minute still in progress is left out until it is complete. */
  sampledAt?: string
  timeZone?: string
}

/**
 * The last 60 minutes of request rate (05-frontend.md section 11): a single line with a 10% area
 * wash, no legend (one series). A `<details>` table twin gives the non-visual equivalent
 * (05-frontend.md section 12).
 */
export function RequestRateChart({
  series,
  startedAt,
  sampledAt,
  timeZone,
}: RequestRateChartProps) {
  const palette = useChartPalette()
  const reducedMotion = useReducedMotion()
  const points = sinceStart(series, startedAt, sampledAt)
  const hasData = points.some((point) => point.requests > 0)

  if (!hasData) {
    return (
      <p className="py-8 text-center text-sm text-muted">Collecting the first minute of data…</p>
    )
  }

  const data = points.map((point) => ({ ...point, label: formatTime(point.minute, timeZone) }))

  return (
    <div className="space-y-3">
      <div style={{ width: '100%', height: 220 }}>
        <ResponsiveContainer>
          <AreaChart data={data} margin={{ top: 8, right: 12, left: 0, bottom: 0 }}>
            <CartesianGrid stroke={palette.grid} vertical={false} />
            <XAxis
              dataKey="label"
              tick={axisTickStyle}
              tickLine={false}
              stroke={palette.grid}
              minTickGap={24}
            />
            <YAxis
              width={44}
              tick={axisTickStyle}
              tickLine={false}
              axisLine={false}
              tickFormatter={formatAxisNumber}
              allowDecimals={false}
            />
            <Tooltip
              {...chartTooltipStyle}
              cursor={{ stroke: 'var(--border-strong)' }}
              labelFormatter={(label) => (typeof label === 'string' ? label : '')}
              formatter={(value) => `${formatNumber(Number(value))} requests`}
            />
            <Area
              type="monotone"
              dataKey="requests"
              name="Requests"
              stroke={palette.accent}
              fill={palette.accent}
              fillOpacity={0.1}
              strokeWidth={2}
              isAnimationActive={!reducedMotion}
            />
          </AreaChart>
        </ResponsiveContainer>
      </div>
      <details>
        <summary className="cursor-pointer text-sm text-muted">View as table</summary>
        <Table caption="Requests per minute, last 60 minutes" captionHidden className="mt-2">
          <TableHead>
            <TableRow>
              <TableHeaderCell>Minute</TableHeaderCell>
              <TableHeaderCell numeric>Requests</TableHeaderCell>
            </TableRow>
          </TableHead>
          <TableBody>
            {points.map((point) => (
              <TableRow key={point.minute}>
                <TableCell label="Minute">{formatTime(point.minute, timeZone)}</TableCell>
                <TableCell numeric label="Requests">
                  {formatNumber(point.requests)}
                </TableCell>
              </TableRow>
            ))}
          </TableBody>
        </Table>
      </details>
    </div>
  )
}
