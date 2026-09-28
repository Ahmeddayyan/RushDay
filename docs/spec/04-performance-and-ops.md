# RushDay v1 specification: 04. Performance and operations

Scope: the enrolment concurrency fix, query shapes, caching, connection pool and timeouts, load shedding, metrics, the
ops snapshot, health and startup, the k6 changes, and how before/after evidence is captured. Decisions D9–D15, D18,
D19 of `00-overview.md` apply.

## 1. Baseline to beat (v0, `docs/load-results/2026-09-27-v0-baseline.md`, local laptop)

| Scenario | v0 result |
|---|---|
| `enrolment-rush.js` (500 VUs, 30 places) | 154 accepted (124 oversold), 84 × 409, 262 errors, 260 TCP refusals, `http_req_failed` 69%, p95 729 ms, Postgres 53300 pool exhaustion |
| `results-day.js` (ramp to 800 rps) | 104,399 requests, 0 failures, p95 6.5 ms, ~15 queries per dashboard |
| `dashboard-knee.js` | knee between 800 and 1,000 rps; throughput ceiling 700–900 rps; at 2,000 rps p95 7.3 s, 8.3% failed, 36,723 dropped iterations; 417 more 53300 errors |

The v1 targets are the checklist items in `00-overview.md` section 8 ("Correctness under load").

## 2. The enrolment concurrency fix (`Infrastructure/Enrolments/EnrolmentService.cs`)

### 2.1 Enrol (`EnrolAsync(studentId, moduleCode, actor, EnrolOptions { Override, ForceCapacity, Reason })`)

Fast path before any transaction: load the module row (`AsNoTracking`); when `!IsActive` → 404; when
`enrolled_count >= capacity` and not `ForceCapacity` → 409 `module-full` immediately (no lock taken; this is how 470
losers get a sub-10 ms answer). Then:

```
BEGIN (ReadCommitted)
1  SELECT id FROM students WHERE id = @student FOR UPDATE            -- serialises this student's own requests
2  windowOpen = Override || EnrolmentWindowCache.IsOpen(academicYear, module.Semester, now)
3  existing   = SELECT * FROM enrolments WHERE student_id = @student AND module_id = @module
4  credits    = SELECT coalesce(sum(m.credits),0) FROM enrolments e JOIN modules m ON m.id = e.module_id
                WHERE e.student_id = @student AND e.status = 'Active' AND m.semester = @semester
5  decision   = EnrolmentRules.Evaluate(module, module.EnrolledCount, credits, existing?.Status == Active, windowOpen)
                (Override skips WindowClosed and CreditLimitExceeded)
   if decision != Accepted → ROLLBACK, map to 409/422
6  if existing is Withdrawn:
       UPDATE enrolments SET status='Active', enrolled_at=@now, withdrawn_at=NULL, source=@source,
              created_by_user_id=@actor, updated_at=@now WHERE id=@existing AND status='Withdrawn'
       0 rows → ROLLBACK → 409 already-enrolled           (lost a race for the same row)
   else:
       INSERT INTO enrolments (...) VALUES (...)          -- 23505 unique violation → ROLLBACK → 409 already-enrolled
7  AuditWriter.Record(enrolment.created | enrolment.admin_created)
8  claimed = ForceCapacity
       ? UPDATE modules SET capacity = capacity + 1, enrolled_count = enrolled_count + 1, updated_at=@now WHERE id=@module
       : UPDATE modules SET enrolled_count = enrolled_count + 1, updated_at=@now WHERE id=@module AND enrolled_count < capacity
   claimed == 0 → ROLLBACK → 409 module-full
COMMIT → 201 { moduleCode, enrolledAt, placesRemaining = capacity - enrolled_count (from RETURNING) }
```

Metrics: `rushday.enrolments.accepted`, `rushday.enrolments.rejected{reason=module_full|already_enrolled|window_closed|credit_limit}`,
`rushday.enrolments.duration` (ms). Step 8 uses `RETURNING capacity, enrolled_count` so the response needs no extra read.

Why it is correct under PostgreSQL's default READ COMMITTED: an `UPDATE` locks each row it targets; when another
transaction has already updated the same module row and not yet committed, ours blocks, and on its commit PostgreSQL
re-evaluates the `WHERE` clause against the new row version (EvalPlanQual). The check `enrolled_count < capacity` and
the increment are therefore one atomic step on the latest committed value, and at most `capacity` transactions ever
satisfy it. Step 8 runs last so the hot module row is locked only for that statement plus the commit, while the
losers' inserts are rolled back with their transaction. The unique index on (`student_id`, `module_id`) makes duplicate
rows impossible regardless of ordering, and the conditional reactivation update does the same for withdrawn rows.
Lock order is always student → enrolment → module in both enrol and withdraw, so there is no deadlock.

### 2.2 Withdraw (`WithdrawAsync(studentId, moduleCode, actor, WithdrawOptions { Override, Reason })`)

```
BEGIN
1  SELECT id FROM students WHERE id=@student FOR UPDATE
2  enrolment = active row for (student, module) → none → 404 not-enrolled
3  if !Override: window.AllowsWithdrawalAt(now) else 409 withdrawal-deadline-passed
               no grade with status IN ('Submitted','Published') for (student, module) else 409 results-exist
4  UPDATE enrolments SET status='Withdrawn', withdrawn_at=@now, updated_at=@now WHERE id=@e AND status='Active'  (0 rows → 404)
5  UPDATE modules SET enrolled_count = enrolled_count - 1, updated_at=@now WHERE id=@module AND enrolled_count > 0
6  AuditWriter.Record(enrolment.withdrawn | enrolment.admin_withdrawn)
COMMIT → 204
```

### 2.3 Reconciliation

Backfill step 1 (`01-domain-and-data.md` section 6) at every start and `POST /api/admin/ops/reconcile` on demand run
the set-based reconciliation and report modules where `enrolled_count > capacity` as a data-quality warning. The
integration test `EnrolmentConcurrencyTests` fires 200 authenticated parallel `POST /api/me/enrolments` at a 30-place
module and asserts exactly 30 × 201, 170 × 409 `module-full`, and
`SELECT enrolled_count, (SELECT count(*) FROM enrolments WHERE module_id = m.id AND status='Active') FROM modules m`
equal to 30 and 30.

## 3. Query shapes

Every query is `AsNoTracking`, projects to a record (no entity materialisation), and is written so the number of round
trips does not depend on the number of rows a student or module has.

| Read | Queries | Shape |
|---|---|---|
| `GET /api/me/dashboard` (`Queries/DashboardQuery.cs`) | **5** | (1) `students WHERE id=@s`; (2) `enrolments e JOIN modules m WHERE e.student_id=@s AND e.status='Active'` → `{ moduleId, code, title, credits, semester, enrolledAt }`; (3) `timetable_slots WHERE module_id IN (@ids)`; (4) `grades g JOIN modules m WHERE g.student_id=@s AND g.status='Published' AND g.published_at <= @now` → `GradeResult`; (5) `announcements WHERE deleted_at IS NULL AND published_at <= @now AND (expires_at IS NULL OR expires_at > @now) AND (scope='University' OR module_id IN (@ids)) ORDER BY pinned DESC, published_at DESC LIMIT 5`. Windows, settings and next publication come from caches. `rushday.dashboard.queries` records 5; `rushday.dashboard.duration` records elapsed ms. Also the `nextPublication` uses cache `publication:next` (60 s): `SELECT min(publish_at) ... WHERE publish_at > @now`. |
| `GET /api/me/results` | 3 | visible grades ⋈ modules; scheduled instants: `SELECT m.semester, min(g.published_at) FROM grades g JOIN modules m ... WHERE g.student_id=@s AND g.status='Published' AND g.published_at > @now GROUP BY m.semester`; active enrolment semesters: `SELECT DISTINCT m.semester FROM enrolments e JOIN modules m ... WHERE e.student_id=@s AND e.status='Active'`. |
| `GET /api/me/timetable` | 1 | slots for active enrolments via join |
| `GET /api/me/enrolments` | 1 | enrolments ⋈ modules, all statuses |
| `GET /api/modules` | 0 on hit | `CatalogueCache` builds the list with 2 queries (modules; module_lecturers ⋈ lecturers) every 30 s; windows from `windows:all` |
| `GET /api/modules/{code}` | 3 | module row (live count); slots; lecturers |
| `GET /api/lecturer/modules` | 3 | assignments ⋈ modules for `lecturer_id`; per-module grade status aggregate (`GROUP BY module_id, status`); active enrolment counts come from `enrolled_count` |
| `GET /api/lecturer/modules/{code}/roster` | 2 | count; page (`enrolments e JOIN students s ... WHERE e.module_id=@m [AND (s.student_number ILIKE @q OR s.full_name ILIKE @q)] ORDER BY e.status, s.student_number LIMIT/OFFSET`) — the join to `module_lecturers` is part of the `WHERE` for lecturers |
| `GET /api/lecturer/modules/{code}/marks` | 2 | enrolments ⋈ students (all statuses) LEFT JOIN grades ⋈ users (entered_by); module status aggregate |
| `GET /api/admin/results` | 3 | modules of the semester with leader (LEFT JOIN module_lecturers role=Leader ⋈ lecturers); grade status counts per module; active counts per module missing marks: `SELECT e.module_id, count(*) FROM enrolments e LEFT JOIN grades g ON g.student_id=e.student_id AND g.module_id=e.module_id WHERE e.status='Active' AND g.id IS NULL GROUP BY e.module_id`; publications for the semester |
| `GET /api/admin/students/{n}` | 5 | student; account; enrolments ⋈ modules; grades ⋈ modules; audit LIMIT 10 |
| `GET /api/admin/audit` | 2 | count; page; every filter maps to an indexed column (`actor_user_id` resolved from username first) |
| `GET /api/announcements` | 2 | the caller's module ids (student: active enrolments; lecturer: assignments); announcements filtered as in the dashboard, LIMIT 50; university-only part served from `announcements:university` (30 s) and merged |

The 15-query v0 dashboard remains in git history at tag `v0-naive` for the before/after comparison.

## 4. Caching (in-process only)

`Microsoft.Extensions.Caching.Hybrid` 10.10.0 with **no** distributed backend (`HybridCache` gives stampede protection
and typed entries; the L2 is simply not registered). Every entry is small (the catalogue is ~121 rows).

| Key | Content | TTL | Invalidated by |
|---|---|---|---|
| `catalogue:all` | `ModuleSummary[]` for active modules (counts from `enrolled_count`) | 30 s | `POST/PUT /api/admin/modules*` |
| `windows:all` | all `enrolment_windows` rows | 60 s | window mutations |
| `settings` | `academic_settings` row | 60 s | `PUT /api/admin/settings` |
| `publication:next` | earliest future `publish_at` with year/semester, or null | 60 s | publish, reschedule |
| `announcements:university` | visible university announcements | 30 s | admin announcement mutations |
| `lecturer-modules:{lecturerId}` | module codes assigned | 60 s | `PUT /api/admin/modules/{code}/lecturers` |
| `public-status` (OutputCache) | `GET /api/public/status` body | 10 s | time |

Metric `rushday.cache.requests{cache, result=hit|miss}` is recorded by each cache wrapper. Authenticated responses
are never output-cached (personal data, `Cache-Control: no-store`). Static assets are cached by fingerprint
(`05-frontend.md` section 4).

## 5. Pool, timeouts and load shedding

- Npgsql connection string builder (`AddRushDayPersistence`) applies these only when the incoming string does not
  already set them: `Maximum Pool Size = Database:MaxPoolSize` (20 Production, 40 Development), `Minimum Pool Size=2`,
  `Timeout=5` (seconds waiting for a pooled connection), `Command Timeout=10`, `Connection Idle Lifetime=60`,
  `Connection Pruning Interval=10`, `Keepalive=30`, `Application Name=rushday-api`, `Include Error Detail=false`.
  Neon's free compute allows about 112 connections; 20 leaves room for Neon's own tooling and a second deploy during
  Render's overlap window. Local PostgreSQL has `max_connections=100`; 40 is safe for load runs.
- Pool wait exhaustion surfaces as `NpgsqlException` (transient) → 503 `server-busy` with `Retry-After: 2` in under 5 s,
  never as a 500, and increments `rushday.db.pool_wait_timeouts`. Zero Postgres 53300 errors must appear in the API log
  during `dashboard-knee.js`.
- Global concurrency limiter on `/api` (`02-api.md` section 5): permit 24 / queue 96 on Render, 64 / 256 locally; sized
  so that at most ~pool-size requests execute queries concurrently while a bounded queue absorbs bursts; beyond it
  the client gets 503 + `Retry-After: 1` in milliseconds. `dashboard-knee.js` reruns must show the tail flatten and
  `dropped_iterations` replaced by counted 503s.
- `AddRequestTimeouts`: default 15 s (`TimeoutStatusCode = 503`, slug `timeout`); login 20 s; CSV export 60 s.
- Kestrel limits as in `03-security.md` section 3; `Backlog = 1024` tests the TCP-refusal hypothesis from the baseline
  (the rerun records `http_req_failed` from connection errors separately via k6's `http_req_connecting` and the
  `errored` counter).
- Process: `<ServerGarbageCollection>false</ServerGarbageCollection>`, `<ConcurrentGarbageCollection>true</ConcurrentGarbageCollection>`,
  `<TieredPGO>true</TieredPGO>` (default), `InvariantGlobalization` stays. Workstation GC keeps the heap small on a
  512 MB container. `PublishReadyToRun` is Could (faster cold start, larger image) and is not enabled in v1.
- Memory budget under the 800 rps run: working set below 350 MB (measured through `/api/admin/ops/metrics` and recorded).

## 6. Metrics and the ops snapshot

### 6.1 Instruments (`Observability/RushDayMetrics.cs`, `Meter "RushDay"`)

| Instrument | Type | Tags |
|---|---|---|
| `rushday.enrolments.accepted` | Counter<long> | |
| `rushday.enrolments.rejected` | Counter<long> | `reason` |
| `rushday.enrolments.duration` | Histogram<double> ms | |
| `rushday.dashboard.duration` | Histogram<double> ms | |
| `rushday.dashboard.queries` | Histogram<int> | |
| `rushday.auth.logins` | Counter<long> | `outcome` = success, failed, locked_out |
| `rushday.auth.lockouts` | Counter<long> | |
| `rushday.cache.requests` | Counter<long> | `cache`, `result` |
| `rushday.load_shed.rejected` | Counter<long> | `policy` = api, login, enrol, write |
| `rushday.db.pool_wait_timeouts` | Counter<long> | |
| `rushday.grades.saved` | Counter<long> | |
| `rushday.results.published` | Counter<long> | |

Built-in meters listened to: `Microsoft.AspNetCore.Hosting` (`http.server.request.duration` with
`http.response.status_code`, `http.server.active_requests`), `Microsoft.AspNetCore.Server.Kestrel`
(`kestrel.active_connections`, `kestrel.queued_connections`, `kestrel.rejected_connections`),
`Microsoft.AspNetCore.RateLimiting` (`aspnetcore.rate_limiting.requests` with `aspnetcore.rate_limiting.result`),
`Npgsql` (`db.client.connections.usage` with `state`, `db.client.connections.max`,
`db.client.connections.pending_requests`, `db.client.connections.timeouts`).

### 6.2 `MetricsSnapshotService` (hosted singleton)

A `MeterListener` subscribes to the meters above and aggregates into one-minute buckets, 60 deep (ring buffer;
bounded to a few hundred KB). Counters keep per-bucket deltas and a running total; histograms keep count, sum, min,
max and a fixed-boundary histogram (1, 2, 5, 10, 20, 50, 100, 200, 500, 1000, 2000, 5000, 10000 ms) from which p50,
p95 and p99 are interpolated. Observable gauges (pool usage, active requests) are sampled every 5 s into the current
bucket's last value. Process facts (`Environment.WorkingSet`, `GC.GetTotalMemory(false)`,
`ThreadPool.ThreadCount`) are read at snapshot time.

### 6.3 `OpsSnapshot` (`GET /api/admin/ops/metrics`)

```ts
OpsSnapshot {
  sampledAt: string; startedAt: string; uptimeSeconds: number; commit: string; environment: string;
  runtime: { dotnetVersion: string; gcMode: 'workstation' | 'server'; maxPoolSize: number;
             rateLimiting: { maxConcurrent: number; maxQueued: number; loginPerUserPerMinute: number; enrolPerUserPer10s: number; writePerUserPerMinute: number } };
  process: { workingSetBytes: number; gcHeapBytes: number; threadPoolThreads: number };
  http: { inFlight: number;
          last60s: { requests: number; perSecond: number; p50Ms: number; p95Ms: number; p99Ms: number;
                     status2xx: number; status4xx: number; status5xx: number; rateLimited429: number; shed503: number } };
  db: { poolMax: number; poolBusy: number; poolIdle: number; pendingRequests: number; waitTimeoutsTotal: number };
  cache: [{ name: string; hits: number; misses: number }];
  enrolment: { accepted: number; rejected: { moduleFull: number; alreadyEnrolled: number; windowClosed: number; creditLimit: number }; p95Ms: number };
  dashboard: { p95Ms: number; queriesPerRequest: number };
  auth: { loginsSucceeded: number; loginsFailed: number; lockouts: number };
  series: [{ minute: string; requests: number; p95Ms: number; p99Ms: number; status5xx: number; shed503: number; rateLimited429: number }];  // 60 points, oldest first
  backfills: [{ name: string; completedAt: string; rowsAffected: number; notes: string | null }];
  dataQuality: { modulesOverCapacity: [{ code: string; capacity: number; enrolledCount: number }]; enrolledCountDrift: number };
}
```

Counters are totals since process start; `last60s` is computed from the last bucket. The SPA polls every 5 s while the
ops page is visible (`05-frontend.md`). `dataQuality` runs two cheap queries (over-capacity modules; drift count between
`enrolled_count` and active enrolments) on each call; the ops page is admin-only and polled by one person.

## 7. Health and startup

- `GET /api/health/live` returns `{ status: "Healthy" }` without touching dependencies; it is `render.yaml`'s
  `healthCheckPath`.
- `GET /api/health/ready` runs `AddDbContextCheck<RushDayDbContext>()` and writes JSON via `ResponseWriter`; 503 when
  the database is unreachable. The ops overview shows it as `database: 'ok' | 'degraded'`.
- Startup (`Startup/StartupTasks.RunAsync`): migrate → seed → backfills, each logged with elapsed time; a failure aborts
  startup so Render keeps the previous instance running (its deploy health check never passes). Under
  `--migrate-and-seed` the process exits after the backfills.
- Logging: JSON console in Production with `traceId`, `userId`, `role`, `route`, `statusCode`, `elapsedMs` per request
  (`Startup/RequestLoggingMiddleware.cs`, one line per request at Information; health checks at Debug).

## 8. k6 changes (`load/k6`)

All scenarios authenticate through the real login. Development configuration relaxes the limiters so one machine can
sign in hundreds of students; the load-results document states this.

`load/k6/lib/auth.js` (new):

```js
export function csrf(cookieHeader)            // GET /api/auth/csrf → { token, cookie } (rushday.csrf=...)
export function login(username, password)     // csrf → POST /api/auth/login with X-CSRF-TOKEN → session
export function loginMany(usernames, password, batchSize = 25) // http.batch chunks → sessions[]
export function params(session, extra)        // { headers: { Cookie, 'X-CSRF-TOKEN', 'Content-Type': 'application/json', ...extra.headers }, tags: extra.tags }
// session = { username, cookie: 'rushday.auth=...; rushday.csrf=...', csrf }
```

Sessions are created in `setup()` (outside the measured window, `setupTimeout: '5m'`) and passed as data; requests
send an explicit `Cookie` header because k6's per-VU jar does not carry setup cookies. Login requests are tagged
`endpoint: login` and excluded from dashboard thresholds. Passwords come from `-e PASSWORD=` defaulting to
`Student-Demo-2026!`.

| Script | Change |
|---|---|
| `enrolment-rush.js` | `setup()` logs in `S000001`..`S000{RUSHERS}`; each VU uses `sessions[__VU - 1]` and fires one `POST /api/me/enrolments { moduleCode }`. Counters: `enrolments_accepted` (201), `enrolments_rejected_full` (409 `module-full`), `enrolments_rejected_other` (other 409/422), `enrolments_shed` (429/503), `enrolments_errored` (anything else, including connection errors). Teardown: `GET /api/modules/CS3099` with the first session; prints `CS3099: capacity=30 enrolledCount=30 OVERSOLD=0`. Check `accepted or full` passes on 201 or 409. |
| `results-day.js` | `setup()` logs in a pool of `LOGIN_POOL` (default 200) students spread across the number range; each iteration picks a random session and calls `GET /api/me/dashboard`. Check `dashboard 200`; `results_visible` is a `Rate` metric (informational: it is 0 before the demo's results instant and 1 after), not a threshold. Thresholds unchanged: `http_req_failed{endpoint:dashboard}: rate<0.01`, `http_req_duration{endpoint:dashboard}: p(95)<500, p(99)<1000`. |
| `dashboard-knee.js` | Same pool; adds counter `shed_503` and `rate_limited_429`; `summaryTrendStats` unchanged. The finding is "excess is shed fast" rather than "nothing fails". |
| `login-storm.js` (new) | Ramping arrival rate 5 → 40 logins/s over 2 minutes against `POST /api/auth/login` with a fresh csrf per iteration; counts 200 / 401 / 429; records p95. Documents PBKDF2 cost and the login concurrency guard. |
| `load/results/runs.json` (new) | `[{ file, scenario, version: 'v0' | 'v1', label, ranAt, targetRate?, notes }]` maintained by hand for every committed summary. |
| `load/summarize.mjs` (new) | Reads `runs.json` and each summary, writes `src/RushDay.Web/public/data/load-results.json` (`05-frontend.md` section 8); `--check` exits non-zero when the committed JSON differs (CI). |
| `scripts/load.ps1` | adds `login-storm` and `-Rushers`, `-LoginPool` parameters; unchanged file naming. |
| `load/README.md` | updated for authentication, the relaxed Development limits, and the new scenario. |

Development `appsettings.Development.json` carries `RateLimiting` values from `03-security.md` section 8 and
`Database:MaxPoolSize = 40`; load runs use `dotnet run -c Release` with `ASPNETCORE_ENVIRONMENT=Development`.

## 9. Capturing before/after evidence

Procedure (stage S13 of `06-implementation-plan.md`), all on the same laptop as the baseline, PostgreSQL native:

1. `scripts/reset-db.ps1` (fresh seed, backfills, demo accounts); `dotnet run --project src/RushDay.Api -c Release`.
2. `scripts/load.ps1 enrolment-rush` → expect `accepted=30`, `OVERSOLD=0`, `enrolments_errored=0`. Record the API log
   grep for `53300` (must be empty). Reset and repeat once to show repeatability.
3. `scripts/load.ps1 results-day` → thresholds pass; note `rushday.dashboard.queries` from `/api/admin/ops/metrics`
   (`queriesPerRequest = 5`) and `process.workingSetBytes`.
4. `scripts/load.ps1 dashboard-knee -Rate 1000`, `2000`, `3000`, `4000` → table of achieved rate, p50/p95/p99,
   `shed_503`, `dropped_iterations`; API log grep for `53300` empty.
5. `scripts/load.ps1 login-storm` → 200/429 counts and p95.
6. Should: the injected-latency experiment (Toxiproxy 2.x Windows binary, 3 ms latency toxic on 5433 → 5432,
   `ConnectionStrings__RushDay` pointed at 5433) rerunning `results-day` on tag `v0-naive` and on v1 to show the
   N+1 cost with a realistic round trip.
7. Add each summary to `load/results/runs.json`, run `node load/summarize.mjs`, commit
   `src/RushDay.Web/public/data/load-results.json`.
8. Write `docs/load-results/2026-10-xx-v1-hardened.md`: setup table (unchanged machine), one section per scenario with
   the v0 and v1 numbers side by side, the log evidence, memory, and a "what changed" list linking ADRs.
9. Update `README.md` "Findings so far" with the v1 numbers and link the new document.

ADRs written in stage S11 (each links the before and after runs and the spec section):

| ADR | Title |
|---|---|
| 0007 | Identity with an EF Core store and cookie sessions over JWT (Context opens with the story sentence pair) |
| 0008 | Atomic conditional update for module capacity |
| 0009 | Every route under `/api`; removal of the unauthenticated routes and the interim page |
| 0010 | Pool sizing, bounded waits and concurrency-limiter load shedding |
| 0011 | Set-based dashboard queries and in-process caching |
| 0012 | Results publication as a stored instant; set-based demo account backfill with a shared hash |

ADR 0003 gains a note that a native PostgreSQL install is an equal alternative to Docker and that the integration
tests support both (`RUSHDAY_TEST_CONNECTION`); ADR 0006 gains a note that Render deploys on `checksPass`.
