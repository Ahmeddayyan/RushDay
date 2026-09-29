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

import { formatNumber } from '@/lib/format'

import { useReducedMotion } from '../useReducedMotion'
import { ChartCard } from './ChartCard'
import { ChartTable, type ChartTableColumn } from './ChartTable'
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

function SmallMultiple({
  title,
  points,
  beforeKey,
  afterKey,
  unit,
  palette,
  reducedMotion,
}: {
  title: string
  points: KneePoint[]
  beforeKey: 'beforeP95' | 'beforeServed' | 'beforeDropped'
  afterKey: 'afterP95' | 'afterServed' | 'afterDropped'
  unit: string
  palette: ChartPalette
  reducedMotion: boolean
}) {
  const hasAfter = points.some((point) => point[afterKey] !== undefined)
  const data = points.map((point) => ({
    targetRate: point.targetRate,
    'Before (v0)': point[beforeKey],
    'After (v1)': point[afterKey],
  }))

  return (
    <div>
      <p className="mb-1 text-sm font-medium text-text">{title}</p>
      <div style={{ width: '100%', height: 200 }}>
        <ResponsiveContainer>
          <LineChart data={data} margin={{ top: 8, right: 16, left: 0, bottom: 8 }}>
            <CartesianGrid stroke={palette.grid} />
            <XAxis
              dataKey="targetRate"
              tickFormatter={(value: number) => `${formatNumber(value)}/s`}
              stroke={palette.muted}
              fontSize={12}
              label={{
                value: 'Target load (requests/s)',
                position: 'insideBottom',
                offset: -4,
                fontSize: 11,
              }}
            />
            <YAxis stroke={palette.muted} fontSize={12} unit={unit} />
            <Tooltip formatter={(value) => `${formatNumber(Number(value))}${unit}`} />
            <Legend />
            <Line
              type="monotone"
              dataKey="Before (v0)"
              name="Before (v0)"
              stroke={palette.muted}
              strokeWidth={2}
              dot={{ r: 4, fill: 'var(--surface)', stroke: palette.muted, strokeWidth: 2 }}
              isAnimationActive={!reducedMotion}
              connectNulls
            />
            {hasAfter && (
              <Line
                type="monotone"
                dataKey="After (v1)"
                name="After (v1)"
                stroke={palette.accent}
                strokeWidth={2}
                dot={{ r: 4, fill: 'var(--surface)', stroke: palette.accent, strokeWidth: 2 }}
                isAnimationActive={!reducedMotion}
                connectNulls
              />
            )}
          </LineChart>
        </ResponsiveContainer>
      </div>
      {!hasAfter && <p className="text-xs text-muted">After (v1): not yet measured.</p>}
    </div>
  )
}

/**
 * Three small multiples sharing the target-load x-axis (05-frontend.md section 11): where the
 * dashboard's knee sits, before and after. v0 alone shows the knee between 800 and 1,000 requests
 * per second (docs/load-results/2026-09-27-v0-baseline.md); the "after" series appears once a v1
 * dashboard-knee run is committed.
 */
export function DashboardKneeCharts() {
  const results = useLoadResults()
  const palette = useChartPalette()
  const reducedMotion = useReducedMotion()

  const points = buildPoints(results)
  const hasAnyData = points.some(
    (point) => point.beforeP95 !== undefined || point.afterP95 !== undefined,
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
        <div className="grid gap-6 md:grid-cols-3">
          <SmallMultiple
            title="Slowest 5% of requests (ms)"
            points={points}
            beforeKey="beforeP95"
            afterKey="afterP95"
            unit="ms"
            palette={palette}
            reducedMotion={reducedMotion}
          />
          <SmallMultiple
            title="Requests actually served per second"
            points={points}
            beforeKey="beforeServed"
            afterKey="afterServed"
            unit="/s"
            palette={palette}
            reducedMotion={reducedMotion}
          />
          <SmallMultiple
            title="Requests the test could not even send, or that were turned away"
            points={points}
            beforeKey="beforeDropped"
            afterKey="afterDropped"
            unit=""
            palette={palette}
            reducedMotion={reducedMotion}
          />
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
