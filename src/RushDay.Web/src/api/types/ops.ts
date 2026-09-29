/**
 * `OpsSnapshot` (04-performance-and-ops.md section 6.3), served by `GET /api/admin/ops/metrics` and
 * polled every 5 s by `/admin/ops` while the page is visible. Field for field the same shape as the
 * `OpsSnapshotShape` builder of `src/test/factories.ts` (`makeOpsSnapshot`, stage S5).
 */

export interface OpsRateLimiting {
  maxConcurrent: number
  maxQueued: number
  loginPerUserPerMinute: number
  enrolPerUserPer10s: number
  writePerUserPerMinute: number
}

export interface OpsRuntime {
  dotnetVersion: string
  gcMode: 'workstation' | 'server'
  maxPoolSize: number
  rateLimiting: OpsRateLimiting
}

export interface OpsProcess {
  workingSetBytes: number
  gcHeapBytes: number
  threadPoolThreads: number
}

export interface OpsHttpWindow {
  requests: number
  perSecond: number
  p50Ms: number
  p95Ms: number
  p99Ms: number
  status2xx: number
  status4xx: number
  status5xx: number
  rateLimited429: number
  shed503: number
}

export interface OpsHttp {
  inFlight: number
  /** The last complete minute (the minute so far during the process's first minute). */
  last60s: OpsHttpWindow
}

export interface OpsDb {
  poolMax: number
  poolBusy: number
  poolIdle: number
  pendingRequests: number
  waitTimeoutsTotal: number
}

export interface OpsCacheEntry {
  name: string
  hits: number
  misses: number
}

export interface OpsEnrolmentRejected {
  moduleFull: number
  alreadyEnrolled: number
  windowClosed: number
  creditLimit: number
  resultsExist: number
  /** `student_left` + `module_inactive`. */
  other: number
}

export interface OpsEnrolment {
  accepted: number
  rejected: OpsEnrolmentRejected
  p95Ms: number
}

export interface OpsDashboard {
  p95Ms: number
  queriesPerRequest: number
}

export interface OpsAuth {
  loginsSucceeded: number
  loginsFailed: number
  lockouts: number
}

export interface OpsSeriesPoint {
  minute: string
  requests: number
  p95Ms: number
  p99Ms: number
  status5xx: number
  shed503: number
  rateLimited429: number
}

export interface OpsBackfill {
  name: string
  completedAt: string
  rowsAffected: number
  notes: string | null
}

export interface OpsModuleOverCapacity {
  code: string
  capacity: number
  enrolledCount: number
}

export interface OpsDataQuality {
  refreshedAt: string | null
  staleSince: string | null
  modulesOverCapacity: OpsModuleOverCapacity[]
  enrolledCountDrift: number
}

export interface OpsSnapshot {
  sampledAt: string
  startedAt: string
  uptimeSeconds: number
  commit: string
  environment: string
  runtime: OpsRuntime
  process: OpsProcess
  http: OpsHttp
  db: OpsDb
  cache: OpsCacheEntry[]
  enrolment: OpsEnrolment
  dashboard: OpsDashboard
  auth: OpsAuth
  /** 60 points, oldest first. */
  series: OpsSeriesPoint[]
  backfills: OpsBackfill[]
  dataQuality: OpsDataQuality
}

/** `POST /api/admin/ops/reconcile` */
export interface ReconcileResponse {
  modulesCorrected: { code: string; before: number; after: number }[]
}

/** `POST /api/admin/ops/demo-reset`; mapped only when `Demo:Enabled`. */
export interface DemoResetResponse {
  withdrawn: number
}
