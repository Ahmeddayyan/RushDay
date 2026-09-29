import {
  Bar,
  BarChart,
  CartesianGrid,
  Legend,
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
import { useLoadResults } from './useLoadResults'

interface RushRow {
  key: 'v0' | 'v1'
  label: string
  total: number
  gotAPlace: number
  toldFull: number
  turnedAway: number
  failed: number
}

const LABELS = {
  gotAPlace: 'Got a place',
  toldFull: 'Told it was full',
  turnedAway: 'Turned away while busy',
  failed: 'Failed',
} as const

function toRow(
  key: RushRow['key'],
  label: string,
  metrics:
    { accepted?: number; rejectedFull?: number; shed?: number; errored?: number } | undefined,
): RushRow | undefined {
  if (!metrics) return undefined
  const gotAPlace = metrics.accepted ?? 0
  const toldFull = metrics.rejectedFull ?? 0
  const turnedAway = metrics.shed ?? 0
  const failed = metrics.errored ?? 0
  return {
    key,
    label,
    total: gotAPlace + toldFull + turnedAway + failed,
    gotAPlace,
    toldFull,
    turnedAway,
    failed,
  }
}

function pct(value: number, total: number): number {
  return total > 0 ? Math.round((value / total) * 1000) / 10 : 0
}

function RushBars({
  rows,
  palette,
  reducedMotion,
}: {
  rows: RushRow[]
  palette: ChartPalette
  reducedMotion: boolean
}) {
  const data = rows.map((row) => ({
    label: row.label,
    [LABELS.gotAPlace]: pct(row.gotAPlace, row.total),
    [LABELS.toldFull]: pct(row.toldFull, row.total),
    [LABELS.turnedAway]: pct(row.turnedAway, row.total),
    [LABELS.failed]: pct(row.failed, row.total),
  }))

  return (
    <div style={{ width: '100%', height: rows.length * 72 + 40 }}>
      <ResponsiveContainer>
        <BarChart data={data} layout="vertical" margin={{ top: 8, right: 16, left: 8, bottom: 8 }}>
          <CartesianGrid stroke={palette.grid} horizontal={false} />
          <XAxis
            type="number"
            domain={[0, 100]}
            tickFormatter={(value: number) => `${value}%`}
            stroke={palette.muted}
            fontSize={12}
          />
          <YAxis type="category" dataKey="label" width={90} stroke={palette.muted} fontSize={12} />
          <Tooltip formatter={(value) => `${Number(value)}%`} />
          <Legend />
          <Bar
            dataKey={LABELS.gotAPlace}
            name={LABELS.gotAPlace}
            stackId="parts"
            fill={palette.accent}
            isAnimationActive={!reducedMotion}
          />
          <Bar
            dataKey={LABELS.toldFull}
            name={LABELS.toldFull}
            stackId="parts"
            fill={palette.muted}
            isAnimationActive={!reducedMotion}
          />
          <Bar
            dataKey={LABELS.turnedAway}
            name={LABELS.turnedAway}
            stackId="parts"
            fill={palette.accent2}
            isAnimationActive={!reducedMotion}
          />
          <Bar
            dataKey={LABELS.failed}
            name={LABELS.failed}
            stackId="parts"
            fill={palette.critical}
            isAnimationActive={!reducedMotion}
            radius={[0, 4, 4, 0]}
          />
        </BarChart>
      </ResponsiveContainer>
    </div>
  )
}

/**
 * The enrolment rush hero figure and 100%-stacked bars (05-frontend.md section 11): 500 requests
 * racing for CS3099's 30 places, before and after the atomic conditional update (ADR 5, section 00
 * overview; the fix's own ADR is stage S11's).
 */
export function EnrolmentRushChart() {
  const results = useLoadResults()
  const palette = useChartPalette()
  const reducedMotion = useReducedMotion()

  const beforeRun = results.find('enrolment-rush', 'v0')
  const afterRun = results.find('enrolment-rush', 'v1')
  const before = toRow('v0', 'Before (v0)', beforeRun?.metrics)
  const after = toRow('v1', 'After (v1)', afterRun?.metrics)

  if (!before) {
    return (
      <ChartCard
        title="The enrolment rush"
        summary="No enrolment-rush run has been recorded yet."
        whatThisMeans="No enrolment-rush run has been recorded yet."
        chart={null}
        table={null}
        empty="This chart fills in once a v0 or v1 enrolment-rush load test is committed to load/results."
      />
    )
  }

  const oversoldBefore = beforeRun?.metrics.oversold ?? 0
  const oversoldAfter = afterRun?.metrics.oversold
  const heroAfterText = after ? formatNumber(oversoldAfter ?? 0) : 'not yet measured'
  const rows = after ? [before, after] : [before]

  const columns: ChartTableColumn<RushRow>[] = [
    { key: 'label', header: 'Run', render: (row) => row.label },
    { key: 'total', header: 'Requests', numeric: true, render: (row) => formatNumber(row.total) },
    {
      key: 'gotAPlace',
      header: LABELS.gotAPlace,
      numeric: true,
      render: (row) => `${formatNumber(row.gotAPlace)} (${pct(row.gotAPlace, row.total)}%)`,
    },
    {
      key: 'toldFull',
      header: LABELS.toldFull,
      numeric: true,
      render: (row) => `${formatNumber(row.toldFull)} (${pct(row.toldFull, row.total)}%)`,
    },
    {
      key: 'turnedAway',
      header: LABELS.turnedAway,
      numeric: true,
      render: (row) => `${formatNumber(row.turnedAway)} (${pct(row.turnedAway, row.total)}%)`,
    },
    {
      key: 'failed',
      header: LABELS.failed,
      numeric: true,
      render: (row) => `${formatNumber(row.failed)} (${pct(row.failed, row.total)}%)`,
    },
  ]

  return (
    <ChartCard
      title="The enrolment rush"
      summary={`Places given out beyond capacity: ${formatNumber(oversoldBefore)} before, ${heroAfterText} after. Before, ${formatNumber(before.gotAPlace)} of ${formatNumber(before.total)} requests got a place on a module with 30 places; ${formatNumber(before.toldFull)} were correctly told it was full and ${formatNumber(before.failed)} failed outright.`}
      lead={
        <p className="mb-4 text-2xl font-semibold tracking-tight text-text tabular-nums">
          Places given out beyond capacity: {formatNumber(oversoldBefore)} → {heroAfterText}
        </p>
      }
      whatThisMeans={
        after ? (
          <>
            Before, {formatNumber(before.gotAPlace)} students got a place on 30 (oversold by{' '}
            {formatNumber(oversoldBefore)}). After, exactly the places available were given out and
            the rest were told immediately.
          </>
        ) : (
          <>
            Before, {formatNumber(before.gotAPlace)} students got a place on 30 — oversold by{' '}
            {formatNumber(oversoldBefore)}. The "after" run has not been made yet, so this chart
            cannot yet show whether the fix holds under the same load.
          </>
        )
      }
      decisionRecord="ADR 5 records why v0 shipped without a concurrency guard; the fix has its own ADR once it is written (docs/adr)."
      rawSources={[beforeRun?.source, afterRun?.source].filter((value): value is string =>
        Boolean(value),
      )}
      chart={<RushBars rows={rows} palette={palette} reducedMotion={reducedMotion} />}
      table={
        <ChartTable
          caption="The enrolment rush: before and after"
          columns={columns}
          rows={rows}
          rowKey={(row) => row.key}
        />
      }
    />
  )
}
