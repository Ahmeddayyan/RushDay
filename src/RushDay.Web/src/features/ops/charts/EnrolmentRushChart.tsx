import {
  Bar,
  BarChart,
  CartesianGrid,
  Rectangle,
  ResponsiveContainer,
  Tooltip,
  XAxis,
  YAxis,
  type RectangleProps,
} from 'recharts'

import { formatNumber } from '@/lib/format'

import { ChartCard } from './ChartCard'
import { ChartLegend } from './ChartLegend'
import { ChartTable, type ChartTableColumn } from './ChartTable'
import { axisTickStyle, chartTooltipStyle } from './chartStyle'
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

type SegmentKey = 'gotAPlace' | 'toldFull' | 'turnedAway' | 'failed'

const LABELS: Record<SegmentKey, string> = {
  gotAPlace: 'Got a place',
  toldFull: 'Told it was full',
  turnedAway: 'Turned away while busy',
  failed: 'Failed',
}

/**
 * The four outcomes in the order they are stacked, left to right, which is also the legend's
 * order. Colour by job (05-frontend.md section 11): accent for a place given, grey for the correct
 * "full", the second accent for a fast "try again", and the status red only for genuine failures.
 */
const SEGMENTS: { key: SegmentKey; color: (palette: ChartPalette) => string }[] = [
  { key: 'gotAPlace', color: (palette) => palette.accent },
  { key: 'toldFull', color: (palette) => palette.muted },
  { key: 'turnedAway', color: (palette) => palette.accent2 },
  { key: 'failed', color: (palette) => palette.critical },
]

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

/** Rounded data-ends: the first and last non-empty segment of each bar get the 4 px corners. */
function endRadius(row: RushRow, key: SegmentKey): [number, number, number, number] {
  const present = SEGMENTS.filter((segment) => row[segment.key] > 0).map((segment) => segment.key)
  const first = present[0] === key
  const last = present[present.length - 1] === key
  return [first ? 4 : 0, last ? 4 : 0, last ? 4 : 0, first ? 4 : 0]
}

function RushBars({ rows, palette }: { rows: RushRow[]; palette: ChartPalette }) {
  const byKey = new Map(rows.map((row) => [row.key, row]))
  const data = rows.map((row) => ({
    key: row.key,
    label: row.label,
    ...Object.fromEntries(SEGMENTS.map(({ key }) => [LABELS[key], pct(row[key], row.total)])),
  }))

  return (
    <div className="space-y-3">
      <div style={{ width: '100%', height: rows.length * 48 + 36 }}>
        <ResponsiveContainer>
          <BarChart
            data={data}
            layout="vertical"
            barCategoryGap={12}
            margin={{ top: 4, right: 12, left: 4, bottom: 4 }}
          >
            <CartesianGrid stroke={palette.grid} horizontal={false} />
            <XAxis
              type="number"
              domain={[0, 100]}
              ticks={[0, 25, 50, 75, 100]}
              tickFormatter={(value: number) => `${value}%`}
              tick={axisTickStyle}
              tickLine={false}
              stroke={palette.grid}
            />
            <YAxis
              type="category"
              dataKey="label"
              width={96}
              tick={axisTickStyle}
              tickLine={false}
              axisLine={false}
            />
            <Tooltip
              {...chartTooltipStyle}
              cursor={{ fill: 'var(--surface-2)' }}
              formatter={(value) => `${Number(value)}%`}
            />
            {SEGMENTS.map((segment) => (
              <Bar
                key={segment.key}
                dataKey={LABELS[segment.key]}
                name={LABELS[segment.key]}
                stackId="parts"
                fill={segment.color(palette)}
                // 2 px surface gaps between the segments (05-frontend.md section 11).
                stroke="var(--surface)"
                strokeWidth={2}
                barSize={24}
                // A static measurement: nothing to animate, and no half-drawn frame in a screenshot.
                isAnimationActive={false}
                shape={(props: unknown) => {
                  const bar = props as RectangleProps & { payload?: { key?: RushRow['key'] } }
                  const row = bar.payload?.key ? byKey.get(bar.payload.key) : undefined
                  return <Rectangle {...bar} radius={row ? endRadius(row, segment.key) : 0} />
                }}
              />
            ))}
          </BarChart>
        </ResponsiveContainer>
      </div>
      <ChartLegend
        className="pl-1"
        items={SEGMENTS.map((segment) => ({
          label: LABELS[segment.key],
          color: segment.color(palette),
          shape: 'bar' as const,
        }))}
      />
    </div>
  )
}

/** "124 → 0", or "124 → not yet measured" while no v1 run exists: the one number that matters. */
function Hero({ before, after }: { before: number; after: number | undefined }) {
  const sentence = `Places given out beyond capacity: ${formatNumber(before)} before, ${
    after === undefined ? 'not yet measured after' : `${formatNumber(after)} after`
  }.`
  return (
    <div className="mb-5">
      <p className="sr-only">{sentence}</p>
      <div aria-hidden="true">
        <p className="text-sm font-medium text-muted">Places given out beyond capacity</p>
        <div className="mt-1 flex flex-wrap items-end gap-x-5 gap-y-2 tabular-nums">
          <div>
            <p className="text-3xl leading-none font-semibold tracking-tight text-text">
              {formatNumber(before)}
            </p>
            <p className="mt-1.5 text-xs text-muted">Before (v0)</p>
          </div>
          <p className="pb-6 text-xl leading-none text-muted">→</p>
          <div>
            {after === undefined ? (
              <p className="text-base leading-[1.875rem] font-medium text-muted">
                Not yet measured
              </p>
            ) : (
              <p className="text-3xl leading-none font-semibold tracking-tight text-text">
                {formatNumber(after)}
              </p>
            )}
            <p className="mt-1.5 text-xs text-muted">After (v1)</p>
          </div>
        </div>
      </div>
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
  const oversoldAfter = after ? (afterRun?.metrics.oversold ?? 0) : undefined
  const heroAfterText =
    oversoldAfter === undefined ? 'not yet measured' : formatNumber(oversoldAfter)
  const rows = after ? [before, after] : [before]

  const columns: ChartTableColumn<RushRow>[] = [
    { key: 'label', header: 'Run', render: (row) => row.label },
    { key: 'total', header: 'Requests', numeric: true, render: (row) => formatNumber(row.total) },
    ...SEGMENTS.map(({ key }): ChartTableColumn<RushRow> => ({
      key,
      header: LABELS[key],
      numeric: true,
      render: (row) => `${formatNumber(row[key])} (${pct(row[key], row.total)}%)`,
    })),
  ]

  return (
    <ChartCard
      title="The enrolment rush"
      summary={`Places given out beyond capacity: ${formatNumber(oversoldBefore)} before, ${heroAfterText} after. Before, ${formatNumber(before.gotAPlace)} of ${formatNumber(before.total)} requests got a place on a module with 30 places; ${formatNumber(before.toldFull)} were correctly told it was full and ${formatNumber(before.failed)} failed outright.`}
      lead={<Hero before={oversoldBefore} after={oversoldAfter} />}
      whatThisMeans={
        after ? (
          <>
            Before, {formatNumber(before.gotAPlace)} students got a place on 30 (oversold by{' '}
            {formatNumber(oversoldBefore)}). After, exactly the places available were given out and
            the rest were told immediately.
          </>
        ) : (
          <>
            Before, {formatNumber(before.gotAPlace)} students got a place on 30, oversold by{' '}
            {formatNumber(oversoldBefore)}. The "after" run has not been made yet, so this chart
            cannot yet show whether the fix holds under the same load.
          </>
        )
      }
      decisionRecord="ADR 5 records why v0 shipped without a concurrency guard; the fix has its own ADR once it is written (docs/adr)."
      rawSources={[beforeRun?.source, afterRun?.source].filter((value): value is string =>
        Boolean(value),
      )}
      chart={<RushBars rows={rows} palette={palette} />}
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
