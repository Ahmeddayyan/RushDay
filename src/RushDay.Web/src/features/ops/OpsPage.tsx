import { useEffect, useState } from 'react'

import { useOpsMetrics } from '@/api/endpoints/ops'
import type { OpsSnapshot } from '@/api/types/ops'
import {
  Card,
  CardHeader,
  CardTitle,
  ErrorState,
  LoadingRegion,
  PageHeader,
  Skeleton,
} from '@/components/ui'
import { DashboardKneeCharts } from '@/features/ops/charts/DashboardKneeCharts'
import { EnrolmentRushChart } from '@/features/ops/charts/EnrolmentRushChart'
import { LoginStormTiles } from '@/features/ops/charts/LoginStormTiles'
import { ResultsDayTiles } from '@/features/ops/charts/ResultsDayTiles'
import { formatDateTime } from '@/lib/format'
import { useDocumentTitle } from '@/lib/useDocumentTitle'

import { CountersPanel } from './components/CountersPanel'
import { DataQualityPanel } from './components/DataQualityPanel'
import { HealthSummary } from './components/HealthSummary'
import { LatencyChart } from './components/LatencyChart'
import { LiveTiles } from './components/LiveTiles'
import { PoolMeter } from './components/PoolMeter'
import { RequestRateChart } from './components/RequestRateChart'
import { TechnicalDetails } from './components/TechnicalDetails'
import { formatUptime } from './formatUptime'

const HISTORY_LIMIT = 60

/**
 * `/admin/ops` (05-frontend.md section 10): written for a registry manager first and an engineer
 * second. Polls `['admin','ops','metrics']` every 5 s while visible (`queryTimings.opsMetrics`) and
 * keeps the last snapshot on a failed poll, greying the tiles instead of throwing an `ErrorState`.
 */
export function Component() {
  useDocumentTitle('Operations · RushDay')
  const query = useOpsMetrics()

  // "Storing information from previous renders" (react.dev): a sample that differs from the last
  // one seen extends the client-side history, updated during render rather than in an effect, so
  // this never causes the cascading-render pattern `react-hooks/set-state-in-effect` warns against.
  const [history, setHistory] = useState<OpsSnapshot[]>([])
  const [lastSampledAt, setLastSampledAt] = useState<string | null>(null)
  if (query.data && query.data.sampledAt !== lastSampledAt) {
    const data = query.data
    setLastSampledAt(data.sampledAt)
    setHistory((previous) => [...previous, data].slice(-HISTORY_LIMIT))
  }

  // "Sampled {n}s ago" (05-frontend.md section 10 header), while stale "Last sample {n}s ago": the
  // age of the last *successful* sample, so it keeps counting up correctly through a run of failed
  // polls. `query.dataUpdatedAt` is already a reactive value TanStack Query maintains (not an impure
  // call this component makes), so the only impure call is `Date.now()` for "now", which a ticking
  // interval supplies by calling setState from its own callback — the pattern react.dev recommends
  // for subscribing to an external system, rather than reading it directly during render.
  const [nowTick, setNowTick] = useState(() => Date.now())

  useEffect(() => {
    const timer = setInterval(() => setNowTick(Date.now()), 1000)
    return () => clearInterval(timer)
  }, [])

  const sampleAgeSeconds = Math.max(0, Math.round((nowTick - query.dataUpdatedAt) / 1000))

  if (query.isPending) {
    return (
      <div>
        <PageHeader title="Operations" description="The system's live health, in plain language." />
        <LoadingRegion label="operations">
          <div className="space-y-4">
            <Skeleton className="h-24 w-full" />
            <Skeleton className="h-40 w-full" />
            <Skeleton className="h-64 w-full" />
          </div>
        </LoadingRegion>
      </div>
    )
  }

  if (query.isError && history.length === 0) {
    return (
      <div>
        <PageHeader title="Operations" description="The system's live health, in plain language." />
        <ErrorState error={query.error} onRetry={() => void query.refetch()} />
      </div>
    )
  }

  const latest = history[history.length - 1]
  if (!latest) return null
  const previous = history[history.length - 2]
  const stale = query.isError

  return (
    <div className="space-y-6">
      <PageHeader
        title="Operations"
        description="The system's live health, in plain language."
        eyebrow={`Commit ${latest.commit} · ${latest.environment}`}
      >
        <p className="text-sm text-muted">
          Running since {formatDateTime(latest.startedAt)} ({formatUptime(latest.uptimeSeconds)}).{' '}
          <span aria-live="polite">
            {stale
              ? `Last sample ${sampleAgeSeconds}s ago; the server is too busy to answer right now.`
              : `Sampled ${sampleAgeSeconds}s ago.`}
          </span>
        </p>
      </PageHeader>

      <HealthSummary snapshot={latest} previous={previous} stale={stale} />

      <LiveTiles history={history} stale={stale} />

      <PoolMeter db={latest.db} />

      <Card>
        <CardHeader>
          <CardTitle>Requests, last 60 minutes</CardTitle>
        </CardHeader>
        <p className="mb-3 text-xs text-muted">
          History starts when the server last started ({formatDateTime(latest.startedAt)}).
        </p>
        <RequestRateChart series={latest.series} />
      </Card>

      <Card>
        <CardHeader>
          <CardTitle>Latency, last 60 minutes</CardTitle>
        </CardHeader>
        <p className="mb-3 text-xs text-muted">
          History starts when the server last started ({formatDateTime(latest.startedAt)}).
        </p>
        <LatencyChart series={latest.series} />
      </Card>

      <CountersPanel snapshot={latest} />

      <DataQualityPanel dataQuality={latest.dataQuality} />

      <TechnicalDetails snapshot={latest} />

      <div className="space-y-6">
        <h2 className="text-lg font-semibold tracking-tight text-text">The load story</h2>
        <EnrolmentRushChart />
        <DashboardKneeCharts />
        <ResultsDayTiles />
        <LoginStormTiles />
      </div>

      <p className="text-sm text-muted">
        This page updates while it is open. RushDay does not send alerts in this edition; check it
        after a deploy and on results day.
      </p>
    </div>
  )
}
