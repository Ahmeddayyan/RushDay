import { formatDecimal, formatNumber } from '@/lib/format'

import { BeforeAfterStat } from './BeforeAfterStat'
import { ChartCard } from './ChartCard'
import { ChartTable, type ChartTableColumn } from './ChartTable'
import { useLoadResults } from './useLoadResults'

interface ResultsDayRow {
  key: string
  label: string
  caption: string
  detail?: string
  /** Formatted figures; undefined when the run did not record it (before) or has not run (after). */
  before: string | undefined
  after: string | undefined
}

const NOT_RECORDED = 'not recorded for this run'
const NOT_YET_MEASURED = 'not yet measured'

/**
 * Results-day KPI tiles (05-frontend.md section 11): the dashboard held up even at 800 requests per
 * second on v0, because the N+1 query pattern is cheap when the database is on the same machine; the
 * "database round trips per page" figure comes from the code itself (`DashboardTests`, section 6.1 of
 * `04-performance-and-ops.md`), not from a load run, and stays true regardless of whether a v1 run
 * has been captured yet.
 */
export function ResultsDayTiles() {
  const results = useLoadResults()
  const beforeRun = results.find('results-day', 'v0')
  const afterRun = results.find('results-day', 'v1')

  if (!beforeRun) {
    return (
      <ChartCard
        title="Results day"
        summary="No results-day run has been recorded yet."
        whatThisMeans="No results-day run has been recorded yet."
        chart={null}
        table={null}
        empty="This chart fills in once a results-day load test is committed to load/results."
      />
    )
  }

  const peakBefore = beforeRun.targetRate
  const peakAfter = afterRun?.targetRate
  const p95Before = beforeRun.metrics.p95Ms
  const p95After = afterRun?.metrics.p95Ms
  const p99Before = beforeRun.metrics.p99Ms
  const p99After = afterRun?.metrics.p99Ms
  const failedBefore = beforeRun.metrics.failedRate * 100
  const failedAfter = afterRun ? afterRun.metrics.failedRate * 100 : undefined

  const rows: ResultsDayRow[] = [
    {
      key: 'peak',
      label: 'Peak load reached',
      caption: 'requests/s',
      before: peakBefore === undefined ? undefined : `${formatNumber(peakBefore)}/s`,
      after: peakAfter === undefined ? undefined : `${formatNumber(peakAfter)}/s`,
    },
    {
      key: 'p95',
      label: 'Slowest 5% of requests',
      caption: 'p95 ms',
      before: `${formatDecimal(p95Before, 1)} ms`,
      after: p95After === undefined ? undefined : `${formatDecimal(p95After, 1)} ms`,
    },
    {
      key: 'p99',
      label: 'Slowest 1 in 100',
      caption: 'p99 ms',
      before: p99Before === undefined ? undefined : `${formatDecimal(p99Before, 1)} ms`,
      after: p99After === undefined ? undefined : `${formatDecimal(p99After, 1)} ms`,
    },
    {
      key: 'failed',
      label: 'Failed requests',
      caption: 'http_req_failed',
      before: `${formatDecimal(failedBefore, 2)}%`,
      after: failedAfter === undefined ? undefined : `${formatDecimal(failedAfter, 2)}%`,
    },
    {
      key: 'queries',
      label: 'Database round trips per page',
      caption: 'dashboard.queriesPerRequest',
      detail: 'From the code, not a load run.',
      before: '15',
      after: '5',
    },
  ]

  const columns: ChartTableColumn<ResultsDayRow>[] = [
    { key: 'label', header: 'Metric', render: (row) => row.label },
    { key: 'before', header: 'Before (v0)', render: (row) => row.before ?? NOT_RECORDED },
    { key: 'after', header: 'After (v1)', render: (row) => row.after ?? NOT_YET_MEASURED },
  ]

  return (
    <ChartCard
      title="Results day"
      summary={`Results day did not collapse on v0: ${formatDecimal(p95Before, 1)} millisecond slowest-5% response at ${peakBefore ? formatNumber(peakBefore) : 'the peak'} requests per second, and ${formatDecimal(failedBefore, 2)} percent failed.`}
      whatThisMeans="Results day did not collapse even on v0: the dashboard's N+1 query pattern is cheap when the database sits on the same machine. It only becomes expensive once there is real network latency to the database, which is why the fix still matters for the deployed server."
      decisionRecord="See docs/adr for the dashboard query-count fix once published; DashboardTests already asserts exactly 5 queries per request."
      rawSources={[beforeRun.source, afterRun?.source].filter((value): value is string =>
        Boolean(value),
      )}
      chart={
        <div className="grid gap-3 sm:grid-cols-2 lg:grid-cols-3 xl:grid-cols-5">
          {rows.map((row) => (
            <BeforeAfterStat
              key={row.key}
              label={row.label}
              caption={row.caption}
              {...(row.detail ? { detail: row.detail } : {})}
              before={row.before}
              after={row.after}
            />
          ))}
        </div>
      }
      table={
        <ChartTable
          caption="Results day: before and after"
          columns={columns}
          rows={rows}
          rowKey={(row) => row.key}
        />
      }
    />
  )
}
