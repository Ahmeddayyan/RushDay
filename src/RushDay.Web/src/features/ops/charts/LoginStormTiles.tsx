import { StatTile } from '@/components/ui'
import { formatDecimal, formatNumber } from '@/lib/format'

import { ChartCard } from './ChartCard'
import { ChartTable, type ChartTableColumn } from './ChartTable'
import { useLoadResults } from './useLoadResults'

interface LoginStormRow {
  key: string
  label: string
  caption: string
  value: string
}

/**
 * Login-storm KPI tiles (05-frontend.md section 11): `login-storm.js` is new in v1
 * (`04-performance-and-ops.md` section 8), so v0 has no run to compare against. Guard mode measures
 * sign-in throughput under the production rate limits; spray mode checks that a credential-stuffing
 * burst from one address cannot lock out a genuine student signing in from elsewhere.
 */
export function LoginStormTiles() {
  const results = useLoadResults()
  const runs = results.scenario('login-storm')
  const guardRun = runs.find((run) => run.mode === 'guard' || run.mode === undefined)
  const sprayRun = runs.find((run) => run.mode === 'spray')

  if (runs.length === 0) {
    return (
      <ChartCard
        title="The login storm"
        summary="No login-storm run has been recorded yet."
        whatThisMeans="Login-storm is a new v1 scenario, so there is nothing to compare it against from v0: v0 had no sign-in rate limiting to test in the first place."
        chart={null}
        table={null}
        empty="This chart fills in once guard-mode and spray-mode login-storm runs are committed to load/results."
      />
    )
  }

  const rows: LoginStormRow[] = []
  if (guardRun) {
    rows.push(
      {
        key: 'rate',
        label: 'Sign-ins per second reached',
        caption: 'http_reqs rate',
        value: guardRun.metrics.achievedRate
          ? `${formatNumber(Math.round(guardRun.metrics.achievedRate))}/s`
          : 'not recorded',
      },
      {
        key: 'accepted',
        label: 'Accepted',
        caption: 'logins_ok',
        value:
          guardRun.metrics.loginsOk === undefined
            ? 'not recorded'
            : formatNumber(guardRun.metrics.loginsOk),
      },
      {
        key: 'waited',
        label: 'Asked to wait',
        caption: 'logins_rate_limited (429)',
        value:
          guardRun.metrics.loginsRateLimited === undefined
            ? 'not recorded'
            : formatNumber(guardRun.metrics.loginsRateLimited),
      },
      {
        key: 'p95',
        label: 'Slowest 5% of sign-ins',
        caption: 'p95 ms',
        value: `${formatDecimal(guardRun.metrics.p95Ms, 1)} ms`,
      },
    )
  }

  const columns: ChartTableColumn<LoginStormRow>[] = [
    { key: 'label', header: 'Metric', render: (row) => row.label },
    { key: 'value', header: 'Guard mode', render: (row) => row.value },
  ]

  return (
    <ChartCard
      title="The login storm"
      summary="Guard mode measures how many logins per second the production rate limits allow; spray mode checks that a credential-stuffing burst from one address cannot lock out a genuine student."
      whatThisMeans={
        sprayRun
          ? 'The attacker is slowed after 20 wrong passwords from the same address, but the real student, signing in from a different address, still gets in.'
          : 'Guard mode shows how many sign-ins per second the production rate limits allow; a spray-mode run adds the "attacker slowed, real student unaffected" finding.'
      }
      decisionRecord="See docs/adr for the login rate-limiting design once published."
      rawSources={[guardRun?.source, sprayRun?.source].filter((value): value is string =>
        Boolean(value),
      )}
      chart={
        <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-4">
          {rows.map((row) => (
            <StatTile key={row.key} label={row.label} caption={row.caption} value={row.value} />
          ))}
        </div>
      }
      table={
        <ChartTable
          caption="The login storm: guard mode"
          columns={columns}
          rows={rows}
          rowKey={(row) => row.key}
        />
      }
    />
  )
}
