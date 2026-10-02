import type { OpsSnapshot } from '@/api/types/ops'
import { formatDecimal, formatNumber } from '@/lib/format'

import { BackfillsTable } from './BackfillsTable'

export interface TechnicalDetailsProps {
  snapshot: OpsSnapshot
}

function Fact({ label, value }: { label: string; value: string }) {
  return (
    <div className="flex items-baseline justify-between gap-4 py-1.5 text-sm">
      <span className="text-muted">{label}</span>
      <span className="font-mono text-xs text-text">{value}</span>
    </div>
  )
}

/** The collapsed engineering detail at the bottom of `/admin/ops` (05-frontend.md section 10). */
export function TechnicalDetails({ snapshot }: TechnicalDetailsProps) {
  const { process, runtime, dataQuality } = snapshot

  return (
    <details className="rounded-lg border border-border bg-surface p-4 shadow-card">
      <summary className="cursor-pointer text-sm font-semibold text-text">
        Technical details
      </summary>
      <div className="mt-4 space-y-6">
        <div>
          <h3 className="mb-1 text-sm font-semibold text-text">Process</h3>
          <div className="divide-y divide-border">
            <Fact
              label="GC heap"
              value={`${formatDecimal(process.gcHeapBytes / 1_000_000, 1)} MB`}
            />
            <Fact label="Thread-pool threads" value={formatNumber(process.threadPoolThreads)} />
          </div>
        </div>
        <div>
          <h3 className="mb-1 text-sm font-semibold text-text">Runtime</h3>
          <div className="divide-y divide-border">
            <Fact label=".NET" value={runtime.dotnetVersion} />
            <Fact label="GC mode" value={runtime.gcMode} />
            <Fact label="Max pool size" value={formatNumber(runtime.maxPoolSize)} />
            <Fact
              label="Concurrency limit"
              value={`${formatNumber(runtime.rateLimiting.maxConcurrent)} concurrent, ${formatNumber(runtime.rateLimiting.maxQueued)} queued`}
            />
            <Fact
              label="Login rate"
              value={`${formatNumber(runtime.rateLimiting.loginPerUserPerMinute)}/user/minute`}
            />
            <Fact
              label="Enrol rate"
              value={`${formatNumber(runtime.rateLimiting.enrolPerUserPer10s)}/user/10s`}
            />
            <Fact
              label="Write rate"
              value={`${formatNumber(runtime.rateLimiting.writePerUserPerMinute)}/user/minute`}
            />
          </div>
        </div>
        <div>
          <h3 className="mb-1 text-sm font-semibold text-text">Data quality drift</h3>
          <p className="text-sm text-muted">
            Enrolled-count drift (cached counts vs. actual active enrolments):{' '}
            {formatNumber(dataQuality.enrolledCountDrift)}.
          </p>
        </div>
        <div>
          <h3 className="mb-1 text-sm font-semibold text-text">Backfills</h3>
          <BackfillsTable backfills={snapshot.backfills} />
        </div>
      </div>
    </details>
  )
}
