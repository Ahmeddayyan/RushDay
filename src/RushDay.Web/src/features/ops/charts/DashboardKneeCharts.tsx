import {
  CartesianGrid,
  Line,
  LineChart,
  ResponsiveContainer,
  Tooltip,
  XAxis,
  YAxis,
} from 'recharts'

import { formatNumber } from '@/lib/format'

import { ChartCard } from './ChartCard'
import { ChartLegend } from './ChartLegend'
import { ChartTable, type ChartTableColumn } from './ChartTable'
import { axisTickStyle, chartTooltipStyle, formatAxisNumber } from './chartStyle'
import { useChartPalette, type ChartPalette } from './palette'
import type { LoadResultsView } from './useLoadResults'
import { useLoadResults } from './useLoadResults'

interface KneePoint {
  targetRate: number
  beforeP95: number | undefined
  afterP95: number | undefined
  beforeServed: number | undefined
  afterServed: number | undefined
  beforeDropped: number | undefined
  afterDropped: number | undefined
}

const TARGET_RATES = [1000, 2000, 3000, 4000] as const

function buildPoints(results: LoadResultsView): KneePoint[] {
  return TARGET_RATES.map((targetRate) => {
    const before = results.find('dashboard-knee', 'v0', targetRate)
    const after = results.find('dashboard-knee', 'v1', targetRate)
    return {
      targetRate,
      beforeP95: before?.metrics.p95Ms,
      afterP95: after?.metrics.p95Ms,
      beforeServed: before?.metrics.achievedRate,
      afterServed: after?.metrics.achievedRate,
      beforeDropped: before?.metrics.droppedIterations,
      afterDropped: after?.metrics.droppedIterations,
    }
  })
}

function dot(color: string) {
  return { r: 4, fill: 'var(--surface)', stroke: color, strokeWidth: 2 }
}

function SmallMultiple({
  title,
  points,
  beforeKey,
  afterKey,
  unit,
  palette,
}: {
  title: string
  points: KneePoint[]
  beforeKey: 'beforeP95' | 'beforeServed' | 'beforeDropped'
  afterKey: 'afterP95' | 'afterServed' | 'afterDropped'
  unit: string
  palette: ChartPalette
}) {
  const hasAfter = points.some((point) => point[afterKey] !== undefined)
  const data = points.map((point) => ({
    targetRate: point.targetRate,
    'Before (v0)': point[beforeKey],
    'After (v1)': point[afterKey],
  }))

  return (
    <div className="min-w-0">
      {/* Two lines tall whatever the title's length, so the three plots line up. */}
      <p className="min-h-10 text-sm leading-5 font-medium text-text">{title}</p>
      <div style={{ width: '100%', height: 180 }}>
        <ResponsiveContainer>
          <LineChart data={data} margin={{ top: 10, right: 8, left: 0, bottom: 0 }}>
            <CartesianGrid stroke={palette.grid} vertical={false} />
            {/* A numeric axis padded inside the plot, so the first and last markers sit clear of
                the edges and the 4,000 tick has room for its label. */}
            <XAxis
              dataKey="targetRate"
              type="number"
              domain={[TARGET_RATES[0], TARGET_RATES[TARGET_RATES.length - 1] ?? 4000]}
              ticks={[...TARGET_RATES]}
              padding={{ left: 18, right: 18 }}
              tickFormatter={(value: number) => formatNumber(value)}
              tick={axisTickStyle}
              tickLine={false}
              stroke={palette.grid}
            />
            <YAxis
              width={40}
              tickFormatter={formatAxisNumber}
              tick={axisTickStyle}
              tickLine={false}
              axisLine={false}
            />
            <Tooltip
              {...chartTooltipStyle}
              cursor={{ stroke: 'var(--border-strong)' }}
              labelFormatter={(value) => `Target load ${formatNumber(Number(value))} requests/s`}
              formatter={(value) => `${formatNumber(Math.round(Number(value)))}${unit}`}
            />
            {/* Straight segments: four measured points, and nothing is known between them. The
                data is static, so it is not animated and no frame shows a marker ahead of its line. */}
            <Line
              type="linear"
              dataKey="Before (v0)"
              name="Before (v0)"
              stroke={palette.muted}
              strokeWidth={2}
              dot={dot(palette.muted)}
              activeDot={{ r: 5 }}
              isAnimationActive={false}
              connectNulls
            />
            {hasAfter && (
              <Line
                type="linear"
                dataKey="After (v1)"
                name="After (v1)"
                stroke={palette.accent}
                strokeWidth={2}
                dot={dot(palette.accent)}
                activeDot={{ r: 5 }}
                isAnimationActive={false}
                connectNulls
              />
            )}
          </LineChart>
        </ResponsiveContainer>
      </div>
      <p className="mt-1 pl-10 text-center text-xs text-muted">Target load (requests per second)</p>
    </div>
  )
}

/**
 * Three small multiples sharing the target-load x-axis (05-frontend.md section 11): where the
 * dashboard's knee sits, before and after. v0 alone shows the knee between 800 and 1,000 requests
 * per second (docs/load-results/2026-09-27-v0-baseline.md); the "after" series appears once a v1
 * dashboard-knee run is committed. One legend serves all three.
 */
export function DashboardKneeCharts() {
  const results = useLoadResults()
  const palette = useChartPalette()

  const points = buildPoints(results)
  const hasAnyData = points.some(
    (point) => point.beforeP95 !== undefined || point.afterP95 !== undefined,
  )
  const hasAnyAfter = points.some(
    (point) =>
      point.afterP95 !== undefined ||
      point.afterServed !== undefined ||
      point.afterDropped !== undefined,
  )

  if (!hasAnyData) {
    return (
      <ChartCard
        title="Where the dashboard breaks"
        summary="No dashboard-knee run has been recorded yet."
        whatThisMeans="No dashboard-knee run has been recorded yet."
        chart={null}
        table={null}
        empty="This chart fills in once dashboard-knee load tests are committed to load/results."
      />
    )
  }

  const columns: ChartTableColumn<KneePoint>[] = [
    {
      key: 'targetRate',
      header: 'Target load',
      render: (row) => `${formatNumber(row.targetRate)}/s`,
    },
    {
      key: 'beforeP95',
      header: 'Slowest 5% before (ms)',
      numeric: true,
      render: (row) =>
        row.beforeP95 === undefined ? 'not recorded' : formatNumber(Math.round(row.beforeP95)),
    },
    {
      key: 'afterP95',
      header: 'Slowest 5% after (ms)',
      numeric: true,
      render: (row) =>
        row.afterP95 === undefined ? 'not yet measured' : formatNumber(Math.round(row.afterP95)),
    },
    {
      key: 'beforeServed',
      header: 'Served/s before',
      numeric: true,
      render: (row) =>
        row.beforeServed === undefined
          ? 'not recorded'
          : formatNumber(Math.round(row.beforeServed)),
    },
    {
      key: 'afterServed',
      header: 'Served/s after',
      numeric: true,
      render: (row) =>
        row.afterServed === undefined
          ? 'not yet measured'
          : formatNumber(Math.round(row.afterServed)),
    },
    {
      key: 'beforeDropped',
      header: 'Dropped/turned away before',
      numeric: true,
      render: (row) =>
        row.beforeDropped === undefined ? 'not recorded' : formatNumber(row.beforeDropped),
    },
    {
      key: 'afterDropped',
      header: 'Dropped/turned away after',
      numeric: true,
      render: (row) =>
        row.afterDropped === undefined ? 'not yet measured' : formatNumber(row.afterDropped),
    },
  ]

  const rawSources = TARGET_RATES.flatMap((rate) => [
    results.find('dashboard-knee', 'v0', rate)?.source,
    results.find('dashboard-knee', 'v1', rate)?.source,
  ]).filter((value): value is string => Boolean(value))

  return (
    <ChartCard
      title="Where the dashboard breaks"
      summary="On v0, the slowest 5% of requests jumps from 6.5 milliseconds at 800 requests per second to about 2.5 seconds at 1,000, and throughput has a ceiling of roughly 700 to 900 requests per second no matter how much more is offered."
      whatThisMeans="Before, the knee sits sharply between 800 and 1,000 requests per second: past it, offering more load does not get more done, it gets less, because the server spends its time on requests that will time out anyway."
      decisionRecord="See the connection pool, timeout and load-shedding sections of docs/adr once published."
      rawSources={rawSources}
      chart={
        <div className="space-y-4">
          <ChartLegend
            items={[
              { label: 'Before (v0)', color: palette.muted, shape: 'line' },
              {
                label: 'After (v1)',
                color: palette.accent,
                shape: 'line',
                ...(hasAnyAfter ? {} : { missing: 'not yet measured' }),
              },
            ]}
          />
          <div className="grid gap-x-8 gap-y-6 md:grid-cols-3">
            <SmallMultiple
              title="Slowest 5% of requests (ms)"
              points={points}
              beforeKey="beforeP95"
              afterKey="afterP95"
              unit=" ms"
              palette={palette}
            />
            <SmallMultiple
              title="Requests actually served per second"
              points={points}
              beforeKey="beforeServed"
              afterKey="afterServed"
              unit="/s"
              palette={palette}
            />
            <SmallMultiple
              title="Requests the test could not even send, or that were turned away"
              points={points}
              beforeKey="beforeDropped"
              afterKey="afterDropped"
              unit=""
              palette={palette}
            />
          </div>
        </div>
      }
      table={
        <ChartTable
          caption="Where the dashboard breaks: before and after, by target load"
          columns={columns}
          rows={points}
          rowKey={(row) => String(row.targetRate)}
        />
      }
    />
  )
}
