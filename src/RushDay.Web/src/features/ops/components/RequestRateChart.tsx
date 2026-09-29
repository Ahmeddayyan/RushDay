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
import { useChartPalette } from '@/features/ops/charts/palette'
import { useReducedMotion } from '@/features/ops/useReducedMotion'
import { formatNumber, formatTime } from '@/lib/format'

export interface RequestRateChartProps {
  series: OpsSeriesPoint[]
}

/**
 * The last 60 minutes of request rate (05-frontend.md section 11): a single line with a 10% area
 * wash, no legend (one series). A `<details>` table twin gives the non-visual equivalent
 * (05-frontend.md section 12).
 */
export function RequestRateChart({ series }: RequestRateChartProps) {
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
          <AreaChart data={data} margin={{ top: 8, right: 16, left: 0, bottom: 8 }}>
            <CartesianGrid stroke={palette.grid} />
            <XAxis dataKey="label" stroke={palette.muted} fontSize={12} minTickGap={24} />
            <YAxis stroke={palette.muted} fontSize={12} />
            <Tooltip
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
      <details className="mt-2">
        <summary className="cursor-pointer text-sm text-muted">View as table</summary>
        <Table caption="Requests per minute, last 60 minutes" captionHidden className="mt-2">
          <TableHead>
            <TableRow>
              <TableHeaderCell>Minute</TableHeaderCell>
              <TableHeaderCell numeric>Requests</TableHeaderCell>
            </TableRow>
          </TableHead>
          <TableBody>
            {series.map((point) => (
              <TableRow key={point.minute}>
                <TableCell label="Minute">{formatTime(point.minute)}</TableCell>
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
