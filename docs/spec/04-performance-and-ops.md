# RushDay v1 specification: 04. Performance and operations

Scope: the enrolment concurrency fix, query shapes, caching, connection pool and timeouts, load shedding, metrics, the
ops snapshot, health and startup, the k6 changes, and how before/after evidence is captured. Decisions D9–D15, D18,
D19 and D28 of `00-overview.md` apply.

## 1. Baseline to beat (v0, `docs/load-results/2026-09-27-v0-baseline.md`, local laptop)

| Scenario | v0 result |
|---|---|
| `enrolment-rush.js` (500 VUs, 30 places) | 154 accepted (124 oversold), 84 × 409, 262 errors, 260 TCP refusals, `http_req_failed` 69%, p95 729 ms, Postgres 53300 pool exhaustion |
| `results-day.js` (ramp to 800 rps) | 104,399 requests, 0 failures, p95 6.5 ms, ~15 queries per dashboard |
| `dashboard-knee.js` | knee between 800 and 1,000 rps; throughput ceiling 700–900 rps; at 2,000 rps p95 7.3 s, 8.3% failed, 36,723 dropped iterations; 417 more 53300 errors |

The v1 targets are the checklist items in `00-overview.md` section 8 ("Correctness under load").

## 2. The enrolment concurrency fix (`Infrastructure/Enrolments/EnrolmentService.cs`)

### 2.1 Enrol (`EnrolAsync(studentId, moduleCode, actor, EnrolOptions { Override, ForceCapacity, Reason })`)

Fast path before any transaction: load the module row (`AsNoTracking`); unknown code → 404 `module-not-found`; when
`!IsActive` → 409 `module-inactive`; when `enrolled_count >= capacity` and not `ForceCapacity` → 409 `module-full`
immediately (no lock taken: this is how most losers of a rush are answered without waiting on a row lock; their latency
is what the machine's queueing and CPU make it, about 230 ms on average and 281 ms at p95 for the 270 losers of a
300-student rush on the laptop in the S4 review, not a few milliseconds, and section 9 reports the measured figure). The
settings year (`SettingsCache`) and every window (`windows:all`) are read from caches here, before the transaction, so
no cache fill runs under a row lock; the year the enrolment is stamped and counted with is the one read under a share
lock in step 1. `enrolled_count` is the current year's count (D28). Then:

```
BEGIN (ReadCommitted)
1  SELECT id, left_at FROM students WHERE id = @student FOR NO KEY UPDATE   -- serialises this student's own requests; NO KEY
                                                                         -- lets foreign-key checks (FOR KEY SHARE) through
   left_at IS NOT NULL → ROLLBACK → 409 student-left
   currentYear = SELECT academic_year FROM academic_settings WHERE id = 1 FOR SHARE
                 -- a year change cannot commit before this transaction ends; when the year differs from the cached one
                 -- (a change committed while this request waited), the window is looked up again for currentYear in the
                 -- windows read before the transaction
2  windowOpen = Override || window(currentYear, module.Semester).IsOpenAt(now)
3  existing   = SELECT * FROM enrolments WHERE student_id = @student AND module_id = @module
   current    = existing?.Status == Active AND existing.AcademicYear == @currentYear
   hasResult  = EXISTS (SELECT 1 FROM grades WHERE student_id = @student AND module_id = @module AND status IN ('Submitted','Published'))
   !current AND hasResult → ROLLBACK → 409 results-exist   (a completed module is not retaken in v1, Override included)
4  credits    = SELECT coalesce(sum(m.credits),0) FROM enrolments e JOIN modules m ON m.id = e.module_id
                WHERE e.student_id = @student AND e.status = 'Active' AND e.academic_year = @currentYear AND m.semester = @semester
5  decision   = EnrolmentRules.Evaluate(module, module.EnrolledCount, credits, alreadyEnrolled: current, windowOpen, ignoreCreditLimit: Override)
   if decision != Accepted → ROLLBACK, map to 409/422
6  if existing is not null (Withdrawn, or Active in an earlier year without a result):
       UPDATE enrolments SET status='Active', enrolled_at=@now, withdrawn_at=NULL, source=@source, academic_year=@currentYear,
              created_by_user_id=@actor, updated_at=@now
       WHERE id=@existing AND (status='Withdrawn' OR academic_year <> @currentYear)
       0 rows → ROLLBACK → 409 already-enrolled           (lost a race for the same row)
       if existing.academic_year <> @currentYear:         (a grade's year is its enrolment's year, 01 section 3)
           DELETE FROM grades WHERE student_id=@student AND module_id=@module AND status='Draft'
           -- the only grade results-exist lets through; the audit row gets discardedDraft: true when one was deleted.
           -- A reactivation within the same year keeps its draft.
   else:
       INSERT INTO enrolments (..., academic_year, ...) VALUES (..., @currentYear, ...)
       -- 23505 on ix_enrolments_student_id_module_id → ROLLBACK → 409 already-enrolled; a unique violation of any other
       -- row in the batch (the audit row) is an error, never already-enrolled
7  AuditWriter.Record(enrolment.created | enrolment.admin_created { ..., capacityRaised: false }), saved in one batch with
   step 6's insert; a ForceCapacity enrolment's audit rows are inserted by the claiming statement itself (step 8)
8  claimed = ForceCapacity
       ? WITH before AS (SELECT capacity AS c FROM modules WHERE id = @module AND is_active FOR NO KEY UPDATE),
              claim AS (UPDATE modules m SET enrolled_count = m.enrolled_count + 1,
                               capacity = CASE WHEN m.enrolled_count >= m.capacity THEN m.enrolled_count + 1 ELSE m.capacity END,
                               updated_at = @now
                        FROM before WHERE m.id = @module AND m.is_active
                        RETURNING m.capacity, m.enrolled_count, m.capacity <> before.c AS raised, before.c AS previous_capacity),
              created AS (INSERT INTO audit_events (...) SELECT ..., 'enrolment.admin_created', ...,
                                 CAST(@details AS jsonb) || jsonb_build_object('capacityRaised', claim.raised), ... FROM claim),
              raised AS (INSERT INTO audit_events (...) SELECT ..., 'module.updated', ...,
                                jsonb_build_object('capacity', jsonb_build_object('before', claim.previous_capacity,
                                                   'after', claim.capacity), 'reason', @reason), ... FROM claim WHERE claim.raised)
         SELECT capacity, enrolled_count, raised, previous_capacity FROM claim
       : UPDATE modules SET enrolled_count = enrolled_count + 1, updated_at=@now
         WHERE id=@module AND is_active AND enrolled_count < capacity
         RETURNING capacity, enrolled_count, false AS raised
   claimed == 0 rows → ROLLBACK → 409 module-full, or 409 module-inactive when a read after the rollback finds the module
   deactivated since the fast path
COMMIT → 201 { moduleCode, enrolledAt, placesRemaining = capacity - enrolled_count (from RETURNING), capacityRaised = raised (admin route only) }
```

Metrics: `rushday.enrolments.accepted`, `rushday.enrolments.rejected{reason=module_full|already_enrolled|window_closed|credit_limit|results_exist|student_left|module_inactive}`,
`rushday.enrolments.duration` (ms).

Mechanism for step 8: EF Core's `SqlQuery`/`SqlQueryRaw` wraps SQL in a subquery and PostgreSQL rejects a
data-modifying statement inside one, and `ExecuteSqlAsync` returns only a row count, so step 8 runs as an
`NpgsqlCommand` created from `db.Database.GetDbConnection().CreateCommand()` with
`Transaction = db.Database.CurrentTransaction!.GetDbTransaction()` and parameters bound by name; the handler reads
`capacity`, `enrolled_count` and `raised` from the reader. Steps 1, 4, 6 and the withdraw updates use
`ExecuteSqlInterpolatedAsync` / LINQ. `capacityRaised` is true only when the module was full, so the audit
`module.updated` row is written only then (`02-api.md` section 8.5). The forced claim locks the row `FOR NO KEY
UPDATE`, the mode the `UPDATE` itself takes: `FOR UPDATE` conflicts with the `FOR KEY SHARE` that every enrolment
insert's foreign-key check holds on the module row, so two forced enrolments on one module waited for each other's
key-share lock and one died with 40P01 (review S4 C1). A forced override's audit rows are inserted by the claiming
statement (data-modifying CTEs, parameters typed explicitly) rather than saved after it, so on every path the hot row is
locked only for the claim and the commit (review S4 C8; the plain override's row goes in before the claim, with the
insert, because its `capacityRaised` is always false).

Why it is correct under PostgreSQL's default READ COMMITTED: an `UPDATE` locks each row it targets; when another
transaction has already updated the same module row and not yet committed, ours blocks, and on its commit PostgreSQL
re-evaluates the `WHERE` clause against the new row version (EvalPlanQual). The check `enrolled_count < capacity` and
the increment are therefore one atomic step on the latest committed value, and at most `capacity` transactions ever
satisfy it. Step 8 runs last so the hot module row is locked only for that statement plus the commit, while the
losers' inserts are rolled back with their transaction. The unique index on (`student_id`, `module_id`) makes duplicate
rows impossible regardless of ordering, and the conditional reactivation update does the same for withdrawn rows.
Lock order is always student (`FOR NO KEY UPDATE`) → settings row (`FOR SHARE`) → enrolment → module (`FOR NO KEY
UPDATE`, which admits the `FOR KEY SHARE` of other enrolments' foreign-key checks) in enrolment, withdrawal and bulk
withdrawal alike, and a module row is locked only by the last statements, so there is no deadlock
(`EnrolmentLockingTests`, `BulkWithdrawalTests`). The year read under the share lock ties the stamped year and the
claimed count together: a year change (its settings `UPDATE`, then the reconciliation, section 2.3) either commits
before the enrolment reads the year or waits for the enrolment to commit.

### 2.2 Withdraw (`WithdrawAsync(studentId, moduleCode, actor, WithdrawOptions { Override, Reason })`)

```
BEGIN
1  SELECT id FROM students WHERE id=@student FOR NO KEY UPDATE
   currentYear = SELECT academic_year FROM academic_settings WHERE id = 1 FOR SHARE      (as in section 2.1 step 1)
2  enrolment = active row for (student, module) → none → 404 not-enrolled
3  if !Override: enrolment.academic_year = currentYear else 409 withdrawal-deadline-passed (an earlier year's row has no open deadline)
               window.AllowsWithdrawalAt(now) else 409 withdrawal-deadline-passed
               no grade with status IN ('Submitted','Published') for (student, module) else 409 results-exist
4  UPDATE enrolments SET status='Withdrawn', withdrawn_at=@now, updated_at=@now WHERE id=@e AND status='Active'  (0 rows → 404)
5  if enrolment.academic_year = currentYear:     (always true for a student; an administrator may withdraw an earlier year's row)
       UPDATE modules SET enrolled_count = enrolled_count - 1, updated_at=@now WHERE id=@module AND enrolled_count > 0
       0 rows → the count had drifted: Warning EnrolledCountDrift (event 4101) and countDecremented = false on the
       receipt; the ops page's dataQuality.enrolledCountDrift shows it and the reconciliation repairs it
6  AuditWriter.Record(enrolment.withdrawn | enrolment.admin_withdrawn)
COMMIT → 204
```

The windows (for step 3) and the cached year are read before the transaction, as in section 2.1. `WithdrawAsync`
withdraws one row: in its own transaction, or under a savepoint in a caller's.

`POST /api/admin/modules/{code}/trim-to-capacity` (current-year active rows, latest `enrolled_at` first) and
`POST /api/admin/students/{n}/leave` (current-year active rows) call `WithdrawManyAsync(targets, actor, WithdrawOptions
{ Override: true, Reason, Trim | Left })` once, inside the route's transaction, with the (student, module) pairs to
withdraw. It keeps the single-row lock order across rows and takes no savepoint:

```
1  SELECT id FROM students WHERE id = ANY(@students) ORDER BY id FOR NO KEY UPDATE      -- every target student, id order
2  currentYear = SELECT academic_year FROM academic_settings WHERE id = 1 FOR SHARE
3  UPDATE enrolments e SET status='Withdrawn', withdrawn_at=@now, updated_at=@now
   FROM unnest(@students, @modules) t(student_id, module_id), modules m
   WHERE (e.student_id, e.module_id) = (t.student_id, t.module_id) AND e.status='Active' AND m.id = e.module_id
   RETURNING e.id, e.student_id, e.module_id, m.code, e.academic_year                   -- one statement for every row
4  one enrolment.admin_withdrawn audit row per withdrawn row (one batch)
5  SELECT id, enrolled_count FROM modules WHERE id = ANY(@modules) ORDER BY id FOR NO KEY UPDATE
   UPDATE modules m SET enrolled_count = greatest(m.enrolled_count - d.n, 0), updated_at=@now
   FROM unnest(@modules, @counts) d(id, n) WHERE m.id = d.id          -- each module once, last; current-year rows only
   (a count below the rows it loses: Warning EnrolledCountDrift, as in step 5 above)
```

It reads no cache (an override needs no window, and the year comes from step 2), so no cache fill ever runs inside the
caller's transaction. The caller must not lock module rows before the call and should commit right after it: the module
rows stay locked until the route's transaction ends. A student's own withdrawal racing it either finishes first (the
bulk statement finds that row withdrawn and counts it in `notEnrolled`) or queues on its student row and then answers
404 `not-enrolled`; neither deadlocks. A loop of `WithdrawAsync` calls in one transaction broke the order across
iterations (the transaction held a module row from one row while waiting for the next row's student, who waited for
that module: 40P01), and its savepoint per row overflowed the 64-entry subtransaction cache at 64 rows (review S4 C2).
Withdrawal never touches grades: a withdrawn student's draft stays Draft and is never submitted, published or shown
(`02-api.md` section 8.4).

### 2.3 Reconciliation

The `reconcile_enrolled_count` backfill step (`01-domain-and-data.md` section 6, last step) at every start and
`POST /api/admin/ops/reconcile` on demand run the set-based reconciliation and report modules where `enrolled_count >
capacity` as a data-quality warning; both count only current-year active enrolments. `StartupBackfills.ReconcileEnrolledCountAsync`
runs in one transaction (the caller's when there is one: the startup step's, or the settings year change's after its
settings `UPDATE`; else its own): `SELECT id FROM modules ORDER BY id FOR NO KEY UPDATE` first, which waits out every
enrolment or withdrawal that has changed a count and not committed, then the two `UPDATE`s as fresh statements whose
snapshots include those commits. Without the lock the counting subquery's snapshot predated the commit the `UPDATE`
waited for, and a stale count overwrote the committed one (review S4 C4: 5 counted for 3 rows became 3 counted for 4).
Callers use the method, never the two statements on their own. The integration test
`EnrolmentConcurrencyTests` creates its own module directly through `RushDayDbContext` in the test (`ZZ3001`, spring,
capacity 30, 15 credits, department `ZZ`, active; the admin route arrives only in S6) and invalidates
`catalogue:all`, so it is independent of `EnrolmentTests` (which
uses `S000001`–`S000050` and CS3099 in the same collection), signs in `S000101`–`S000300` (demo sessions), fires 200
parallel `POST /api/me/enrolments { moduleCode: 'ZZ3001' }` and asserts exactly 30 × 201, 170 × 409 `module-full`, and
`SELECT enrolled_count, (SELECT count(*) FROM enrolments WHERE module_id = m.id AND status='Active') FROM modules m WHERE
code = 'ZZ3001'` equal to 30 and 30. The credit-limit case of `EnrolmentTests` uses `S000002` (a year-2 student with no
2026/27 enrolments) and four autumn modules, then asserts the fifth is 422.

## 3. Query shapes

Every query is `AsNoTracking`, projects to a record (no entity materialisation), and is written so the number of round
trips does not depend on the number of rows a student or module has. `currentYear` is the settings year from cache.

| Read | Queries | Shape |
|---|---|---|
| `GET /api/me/dashboard` (`Queries/DashboardQuery.cs`) | **5** | (1) `students WHERE id=@s`; (2) `enrolments e JOIN modules m WHERE e.student_id=@s AND e.status='Active'` (every year) → `{ moduleId, code, title, credits, semester, academicYear, enrolledAt }`, partitioned in memory into `modules` (`academicYear = currentYear`) and `completed` (earlier years); `credits` summed from the current-year part; (3) `timetable_slots t JOIN modules m WHERE t.module_id IN (@currentYearIds) AND m.semester = @currentSemester` (title and semester for `TimetableEntry`); (4) `grades g JOIN modules m JOIN enrolments e ON (e.student_id, e.module_id) = (g.student_id, g.module_id) WHERE g.student_id=@s AND e.status='Active' AND g.status='Published' AND g.published_at <= @now` → `GradeResult` (with `e.academic_year`, `g.corrected_at`), also used to fill `completed[].mark/band` and to compute `canWithdraw`'s results condition together with a `status IN ('Submitted','Published')` flag per module in the same projection; (5) `announcements WHERE deleted_at IS NULL AND published_at <= @now AND (expires_at IS NULL OR expires_at > @now) AND (scope='University' OR module_id IN (@currentYearIds)) ORDER BY pinned DESC, published_at DESC LIMIT 5`. Windows, settings and publications (`publications:brief`) come from caches. `rushday.dashboard.queries` records the value of `DbCommandCounter` for the request (section 6.1); `rushday.dashboard.duration` records elapsed ms. |
| `GET /api/me/results` (`Queries/ResultsQuery.cs`) | 3 | visible grades (`GradeQueries.VisibleToStudents`) ⋈ modules ⋈ enrolments (year); scheduled instants (`GradeQueries.ScheduledInstantsFor`, instants only): `SELECT e.academic_year, m.semester, min(g.published_at) FROM grades g JOIN modules m ... JOIN enrolments e ... WHERE g.student_id=@s AND e.status='Active' AND g.status='Published' AND g.published_at > @now GROUP BY e.academic_year, m.semester`; pending pairs: `SELECT DISTINCT e.academic_year, m.semester FROM enrolments e JOIN modules m ... WHERE e.student_id=@s AND e.status='Active'`. Grouping by (year, semester) is a unit-tested rule in `ResultsQuery` (S4). |
| `GET /api/me/timetable` | 1 | slots ⋈ modules for active current-year enrolments on modules of the current semester |
| `GET /api/me/export.json` | 3 | student; enrolments ⋈ modules (every year and status); visible grades ⋈ modules ⋈ enrolments (+1 audit insert) |
| `GET /api/me/enrolments` | 1 | enrolments ⋈ modules LEFT JOIN a per-module `EXISTS` on submitted/published grades, all statuses and years |
| `GET /api/modules` | 0 on hit | `CatalogueCache` builds the list with 2 queries (modules; module_lecturers ⋈ lecturers) every 30 s; windows from `windows:all` |
| `GET /api/modules/{code}` | 3 | module row (live count); slots; lecturers |
| `GET /api/lecturer/modules` | 3 | assignments ⋈ modules for `lecturer_id`; per-module grade status aggregate over current-year enrolments (`GROUP BY module_id, status, published_at > now`); current-year active enrolment counts (`GROUP BY module_id`) for `MarksStatus.total` (the catalogue's `enrolledCount` still comes from `enrolled_count`) |
| `GET /api/lecturer/modules/{code}/roster` | 2 | count; page (`enrolments e JOIN students s ... WHERE e.module_id=@m AND e.academic_year=@currentYear [AND (s.student_number ILIKE @q \|\| '%' OR s.full_name ILIKE '%' \|\| @q \|\| '%')] ORDER BY e.status, s.student_number LIMIT/OFFSET`) — the join to `module_lecturers` is part of the `WHERE` for lecturers |
| `GET /api/lecturer/modules/{code}/marks` | 3 | summary (`count(*)`, `count(g.id)` over current-year active enrolments LEFT JOIN grades); page (enrolments ⋈ students (all statuses, current year) LEFT JOIN grades ⋈ users (entered_by), same `q` predicate, `ORDER BY e.status, s.student_number LIMIT/OFFSET`); module status aggregate |
| `GET /api/admin/results` | 3 | modules of the semester with leader (LEFT JOIN module_lecturers role=Leader ⋈ lecturers); grade status counts per module over enrolments of `@academicYear`; active counts and missing counts per module: `SELECT e.module_id, count(*) total, count(g.id) entered FROM enrolments e LEFT JOIN grades g ON g.student_id=e.student_id AND g.module_id=e.module_id WHERE e.status='Active' AND e.academic_year=@academicYear GROUP BY e.module_id`; publications for the (year, semester) |
| `GET /api/admin/modules/{code}/roster`, `/marks` | as the lecturer rows | the same `RosterQuery` / `MarksSheetQuery` without the `module_lecturers` predicate, for `@academicYear` |
| `GET /api/admin/students/{n}` | 5 (+1 audit insert) | student; account; enrolments ⋈ modules; grades ⋈ modules ⋈ enrolments; audit LIMIT 10 |
| `GET /api/admin/audit` | 2 | count; page; every filter maps to an indexed column (`actor_user_id` resolved from username first) |
| `GET /api/announcements` | 2 | the caller's module ids (student: current-year active enrolments; lecturer: assignments); announcements filtered as in the dashboard, LIMIT 50; university-only part served from `announcements:university` (30 s) and merged |

The 15-query v0 dashboard remains in git history at tag `v0-naive` for the before/after comparison.

## 4. Caching (in-process only)

`Microsoft.Extensions.Caching.Hybrid` 10.10.0 with **no** distributed backend (`HybridCache` gives stampede protection
and typed entries; the L2 is simply not registered). Every entry is small (the catalogue is ~121 rows).

| Key | Content | TTL | Invalidated by |
|---|---|---|---|
| `catalogue:all` | `ModuleSummary[]` for active modules (counts from `enrolled_count`) | 30 s | `POST/PUT /api/admin/modules*`, trim |
| `windows:all` | all `enrolment_windows` rows | 60 s | window mutations |
| `settings` | `academic_settings` row | 60 s | `PUT /api/admin/settings` (which also invalidates `catalogue:all` and `windows:all` when the year changes) |
| `publications:brief` | `{ next: PublicationBrief \| null, latest: PublicationBrief \| null }` (earliest future `publish_at`; latest past) | 60 s | publish, reschedule, cancel, unpublish, return-to-draft |
| `announcements:university` | visible university announcements | 30 s | admin announcement mutations, publish with `announce` |
| `lecturer-modules:{lecturerId}` | module codes assigned | 60 s | `PUT /api/admin/modules/{code}/lecturers` |
| `public-status` (OutputCache, named policy `PublicStatusCachePolicy`) | `GET /api/public/status` body | 10 s | time |

**Versioned keys** (review S4 C3, C11, D5): `settings`, `catalogue:all` and `announcements:university` are read and
invalidated through `CacheKeys.GetOrCreateVersionedAsync` and `InvalidateVersionedAsync`. The entry lives under
`{key}:v{generation}`; invalidation advances the generation (one counter per key per `HybridCache` instance) and then
removes the retired entry, so a fill that was in flight at the change, and may have read the rows before the commit,
stores its value under a key no reader uses any more. `HybridCache.RemoveAsync` alone does not stop an in-flight fill,
which then served the pre-change value for its whole lifetime (a stale year for 60 s, a deleted announcement for 30 s).
Invalidation runs after the change has committed. `windows:all`, `publications:brief` and `lecturer-modules:*` still
invalidate with `RemoveAsync` (an in-flight fill can outlive their invalidation by up to one lifetime). Correctness of
enrolment never depends on a cache: the year is read under a share lock and capacity is decided by the claim (section
2.1), which also covers a second instance during Render's deploy overlap, whose caches are its own.

**Every cache factory opens its own context** (`await using var db = await contexts.CreateDbContextAsync(ct)` from
the singleton `IDbContextFactory<RushDayDbContext>`, `Startup/RushDayDbContextFactory.cs`), never the caller's scoped
`RushDayDbContext`. HybridCache runs one factory for every caller waiting on a key, and the caller whose request
started it may end (client gone, timeout) and dispose its scope while the others still wait: with a borrowed context
every waiter would get a 500 (`Connection is not open`, an `ObjectDisposedException`, a corrupted reader). This holds
for S2's `SettingsCache`, `EnrolmentWindowCache`, `PublicationCache` and `LecturerModuleCache` and for S4's
`CatalogueCache` and `AnnouncementCache` (`CacheFillTests.A_fill_survives_the_request_that_started_it`). The factory's
options are built from the same registrations as the scoped context (connection string, snake-case names, the
`DbCommandCounter` interceptor), on the root provider. Cache factories run inside `CacheFill` (the wrapper in
`CacheKeys.GetOrCreateAsync`), so their commands are not counted as the request's own (section 6.1).

Client side (`05-frontend.md` section 6): queries never refetch on window focus (20,000 tabs refocusing at 09:00 is a
stampede), and when a results-day countdown reaches zero every open dashboard waits a random 0–30 s before its single
refetch, which spreads 20,000 clients to about 670 requests per second, under the 800 rps the `results-day.js` run
proves. Failed queries retry after `Retry-After` plus up to 1 s of random jitter, never in lockstep.

Metric `rushday.cache.requests{cache, result=hit|miss}` is recorded by each cache wrapper. Authenticated responses
are never output-cached (personal data, `Cache-Control: no-store`); `public-status` is the one named OutputCache
policy, a custom `IOutputCachePolicy` (`Endpoints/PublicEndpoints.cs`: GET and HEAD, 10 s, locking, never stored
unless the response is a 200 without `Set-Cookie`) that does not include the default policy, so authenticated callers
are served from it too (the response never varies by user and sets no cookie). Static assets are cached by
fingerprint (`05-frontend.md` section 4).

## 5. Pool, timeouts and load shedding

- Npgsql connection string builder (`AddRushDayPersistence`) applies these only when the incoming string does not
  already set them: `Maximum Pool Size = Database:MaxPoolSize` (20 Production, 40 Development), `Minimum Pool Size=0`,
  `Timeout=5` (seconds waiting for a pooled connection), `Command Timeout=10`, `Connection Idle Lifetime=60`,
  `Connection Pruning Interval=10`, `Application Name=rushday-api`, `Include Error Detail=false`. **No `Keepalive`
  and no minimum pool in Production**: a keepalive query every 30 s on two always-open connections would stop Neon's
  compute from auto-suspending and burn the free plan's monthly compute hours; instead the first request after a Neon
  resume pays a few hundred milliseconds, and a pooled connection that died during the suspend surfaces as one
  transient `NpgsqlException` mapped to 503 `server-busy` (documented in `docs/deployment.md`). Development may set
  `Keepalive=30` for long k6 runs. Neon's free compute allows about 112 connections; 20 leaves room for Neon's own
  tooling and a second deploy during Render's overlap window. Local PostgreSQL has `max_connections=100`; 40 is safe
  for load runs.
- `Command Timeout=10` is the request path's. Every startup statement (migration on either connection, seed, backfills)
  runs with `Database:StartupCommandTimeoutSeconds` (600) instead (`db.Database.SetCommandTimeout` on `StartupTasks`'
  context and `CommandTimeout` on the migrations connection's options), because the grade backfill rewrites 80,000 rows
  (2.3 s on the laptop) and on Neon's 0.25 CU could exceed 10 s, and a startup timeout aborts every restart
  (`StartupTaskTests`).
- Pool wait exhaustion surfaces as `NpgsqlException` (transient, inner `TimeoutException`) → 503 `server-busy` with
  `Retry-After: 2` in under 5 s, never as a 500, and increments `rushday.db.pool_wait_timeouts` only for Npgsql's
  pool-exhaustion exception (message `The connection pool has been exhausted, ...`, inner `TimeoutException`); a
  command that times out (a lock wait beyond `Command Timeout`, also an inner `TimeoutException`) and a dead
  connection after a resume are 503s without the metric (review S4 C10). Zero Postgres 53300 errors must appear in the
  API log during `dashboard-knee.js`.
- Global concurrency limiter on `/api` except `/api/health/live` and `/api/admin/ops/metrics` (`02-api.md` section 5):
  permit 24 / queue 96 on Render; permit 64 / queue **1,024** locally. The local queue is deliberately larger than the
  500-VU enrolment rush so the rerun reproduces the 30 accepted / 470 fast-409 story without the limiter shedding any
  of it; on Render the smaller queue is what sheds a real stampede. Beyond the queue the client gets 503 +
  `Retry-After: 1` in milliseconds. `dashboard-knee.js` reruns must show the tail flatten and `dropped_iterations`
  replaced by counted 503s.
- `AddRequestTimeouts`: default 15 s (`TimeoutStatusCode = 503`, slug `timeout`); login 20 s; CSV export 60 s.
- Kestrel limits as in `03-security.md` section 3; `Backlog = 1024` tests the TCP-refusal hypothesis from the baseline
  (the rerun records `http_req_failed` from connection errors separately via k6's `http_req_connecting` and the
  `errored` counter).
- Process: `<ServerGarbageCollection>false</ServerGarbageCollection>`, `<ConcurrentGarbageCollection>true</ConcurrentGarbageCollection>`,
  `<TieredPGO>true</TieredPGO>` (default), `InvariantGlobalization` stays (so `timeZone` is validated by shape,
  `02-api.md` section 8.5). Workstation GC keeps the heap small on a 512 MB container. `PublishReadyToRun` is Could
  (faster cold start, larger image) and is not enabled in v1.
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
| `rushday.auth.logins` | Counter<long> | `outcome` = success, failed, locked_out, mfa_required |
| `rushday.auth.lockouts` | Counter<long> | |
| `rushday.cache.requests` | Counter<long> | `cache`, `result` |
| `rushday.load_shed.rejected` | Counter<long> | `policy` = api, login, enrol, write, health_ready, ops_metrics |
| `rushday.db.pool_wait_timeouts` | Counter<long> | |
| `rushday.grades.saved` | Counter<long> | |
| `rushday.results.published` | Counter<long> | |

`Observability/DbCommandCounter.cs` (S2): an EF `DbCommandInterceptor` registered on the `DbContext` that increments
an async-local box for every command the **endpoint** executes. `EndpointCommandCounterMiddleware`, the last middleware
before the endpoint (after authentication, the rate limiter, authorization and the output cache), resets it, so the
security-stamp re-check of the authentication step (four commands when due) is not counted; commands issued inside a
cache factory (`CacheFill`, section 4) are skipped, so a request that happens to fill a cache is not charged for it.
`MeEndpoints.Dashboard` records its value into `rushday.dashboard.queries`. The number is measured, never a constant
(`RequestPipelineTests.Command_counter_counts_only_the_endpoint_queries`). `DashboardTests` asserts the metric equals 5
**and** cross-checks the log: on a host from `factory.DeriveWithCommandLog()` (a derived host that raises
`Microsoft.EntityFrameworkCore.Database.Command` to Information into a fake log collector; the main factory keeps it at
Warning so the seed and the suite do not log every statement), it warms the caches with a first dashboard call, clears
the collector, and counts 5 Information entries of that category for a second call made without moving the clock (so
no stamp re-check and no cache fill runs during it).

Built-in meters listened to: `Microsoft.AspNetCore.Hosting` (`http.server.request.duration` with
`http.response.status_code`, `http.server.active_requests`), `Microsoft.AspNetCore.Server.Kestrel`
(`kestrel.active_connections`, `kestrel.queued_connections`, `kestrel.rejected_connections`),
`Microsoft.AspNetCore.RateLimiting` (`aspnetcore.rate_limiting.requests` with `aspnetcore.rate_limiting.result`),
`Npgsql` 10, which follows the OpenTelemetry database conventions: `db.client.connection.count` (tag
`db.client.connection.state` = `idle` | `used`, plus `db.client.connection.pool.name`), `db.client.connection.max`,
`db.client.connection.npgsql.pending_requests` and `db.client.connection.npgsql.timeouts` (names read from
the Npgsql 10 assembly). `MetricsSnapshotService` also reads the pre-10 names (`db.client.connections.usage` with
`state`, `db.client.connections.max`, `db.client.connections.pending_requests`), so a driver downgrade does not blank
the pool figures.

### 6.2 `MetricsSnapshotService` (hosted singleton)

A `MeterListener` subscribes to the meters above and aggregates into one-minute buckets, 60 deep (ring buffer;
bounded to a few hundred KB). Counters keep per-bucket deltas and a running total; histograms keep count, sum, min,
max and a fixed-boundary histogram (1, 2, 5, 10, 20, 50, 100, 200, 500, 1000, 2000, 5000, 10000 ms) from which p50,
p95 and p99 are interpolated. Observable gauges (pool usage, active requests) are sampled every 5 s into the current
bucket's last value. Process facts (`Environment.WorkingSet`, `GC.GetTotalMemory(false)`,
`ThreadPool.ThreadCount`) are read at snapshot time. **Data quality is refreshed by this service on a 60 s background
timer** (two cheap queries on a scoped `DbContext` with `Command Timeout=2`: over-capacity modules; drift count between
`enrolled_count` and current-year active enrolments; plus the backfill list), never per request, and **only while the
snapshot is being polled**: the timer skips its queries unless `GET /api/admin/ops/metrics` was called within the last
2 minutes (`PollingWindow`), because a query every minute would keep Neon's compute from ever suspending. The first
poll after an idle spell starts a refresh at once (in the background: the request still touches no database), so
`dataQuality.refreshedAt` is null or old in that first answer and current from the next 5-second poll on. A failed
refresh keeps the last value and sets `dataQuality.staleSince`. Bucketing and the polling gate are unit-tested in
`tests/RushDay.UnitTests/Observability/` (S2).

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
  enrolment: { accepted: number; rejected: { moduleFull: number; alreadyEnrolled: number; windowClosed: number; creditLimit: number; resultsExist: number; other: number }; p95Ms: number };   // other = student_left + module_inactive
  dashboard: { p95Ms: number; queriesPerRequest: number };
  auth: { loginsSucceeded: number; loginsFailed: number; lockouts: number };
  series: [{ minute: string; requests: number; p95Ms: number; p99Ms: number; status5xx: number; shed503: number; rateLimited429: number }];  // 60 points, oldest first
  backfills: [{ name: string; completedAt: string; rowsAffected: number; notes: string | null }];
  dataQuality: { refreshedAt: string | null; staleSince: string | null;
                 modulesOverCapacity: [{ code: string; capacity: number; enrolledCount: number }]; enrolledCountDrift: number };
}
```

Counters are totals since process start; `last60s` is the **last complete minute** (the minute so far during the
process's first minute), and `perSecond` divides its requests by the seconds of that minute the process was actually
up, so the first minute after a cold start mid-minute is not understated. The route sits outside the
global concurrency limiter and behind a per-user token bucket (1 per 2 s) so the page keeps answering while every
other request is being shed; it touches no database (data quality comes from the background refresh). The SPA polls
every 5 s while the ops page is visible and keeps the last snapshot when a poll fails (`05-frontend.md`). The SPA
derives the plain-language health summary from `shed503`, `status5xx` and the `waitTimeoutsTotal` delta.

## 7. Health and startup

- `GET /api/health/live` returns `{ status: "Healthy" }` without touching dependencies; it is `render.yaml`'s
  `healthCheckPath` and sits outside every limiter.
- `GET /api/health/ready` runs `AddDbContextCheck<RushDayDbContext>()` and writes JSON via `ResponseWriter`; 503 when
  the database is unreachable. It is inside the global concurrency limiter and under the `health-ready` policy (30
  per minute per address, 503 `server-busy` beyond) so an anonymous loop cannot hold the pool. The ops overview shows
  it as `database: 'ok' | 'degraded'`.
- Startup (`Startup/StartupTasks.RunAsync`): demo guard (`Demo:Enabled` in any environment but Development requires
  `Demo:PublicDemoAcknowledged`, else abort; with it, a Warning every start) → KEK check (outside Development, no
  `DataProtection:KeyEncryptionKey` aborts; the host-filtering and forwarded-headers Warnings are logged here too) →
  migrate (on `ConnectionStrings:Migrations` when set) → key-ring check outside Development (plaintext keys revoked, an
  encrypted default key ensured; `03-security.md` T18) → seed **only in demo mode or under `--migrate-and-seed`** →
  backfills, each logged with elapsed time and every statement under `Database:StartupCommandTimeoutSeconds` (section
  5); a failure aborts startup so Render keeps the previous instance running (its deploy health check never passes).
  `Database:MigrateOnStartup` and `Database:BackfillOnStartup` default to true outside Development (`render.yaml` sets
  both anyway). Under `--migrate-and-seed` the process exits after the backfills.
- Logging: JSON console in Production (no scopes, `03-security.md` section 6) with `traceId`, `userId`, `role`,
  `route`, `statusCode`, `elapsedMs` per request (`Startup/RequestLoggingMiddleware.cs`, one line per request at
  Information; health checks at Debug). The middleware sits outside `UseExceptionHandler`, so `statusCode` is what the
  client received (503 for a transient database failure, 499 for an abandoned request, 500 only for a real failure;
  `RequestPipelineTests.Request_log_records_the_status_the_client_received`), and it takes the route template recorded
  before the endpoint ran, since the exception handler clears the endpoint.

## 8. k6 changes (`load/k6`)

All scenarios authenticate through the real login. Development configuration relaxes the limiters so one machine can
sign in hundreds of students; the load-results document states this. Every v1 script sets
`summaryTrendStats: ['avg', 'med', 'p(95)', 'p(99)', 'max']` so future summaries carry p99.

`load/k6/lib/auth.js` (new):

```js
export function csrf(cookieHeader)            // GET /api/auth/csrf → { token, cookie }
export function login(username, password)     // csrf → POST /api/auth/login with X-CSRF-TOKEN → session
export function loginMany(usernames, password, batchSize = 25) // http.batch chunks → sessions[]
export function params(session, extra)        // { headers: { Cookie, 'X-CSRF-TOKEN', 'Content-Type': 'application/json', ...extra.headers }, tags: extra.tags }
// session = { username, cookie: '<every cookie the csrf and login responses set, joined with "; ">', csrf }
```

`auth.js` never hard-codes cookie names: it collects every `Set-Cookie` of the two responses (`rushday.auth` and
`rushday.csrf` locally, `__Host-rushday.auth` and `__Host-rushday.csrf` against a deployed instance) and replays them.
Sessions are created in `setup()` (outside the measured window, `setupTimeout: '5m'`) and passed as data; requests
send an explicit `Cookie` header because k6's per-VU jar does not carry setup cookies. Login requests are tagged
`endpoint: login` and excluded from dashboard thresholds. Passwords come from `-e PASSWORD=` defaulting to
`Student-Demo-2026!`.

| Script | Change |
|---|---|
| `enrolment-rush.js` | `setup()` logs in `S000001`..`S000{RUSHERS}`; each VU uses `sessions[__VU - 1]` and fires one `POST /api/me/enrolments { moduleCode }`. Counters: `enrolments_accepted` (201), `enrolments_rejected_full` (409 `module-full`), `enrolments_rejected_other` (other 409/422), `enrolments_shed` (429/503), `enrolments_errored` (anything else, including connection errors). Teardown: `GET /api/modules/CS3099` with the first session; prints `CS3099: capacity=30 enrolledCount=30 OVERSOLD=0`. Check `accepted or full` passes on 201 or 409. Expected locally: 30 accepted, 470 `module-full`, 0 shed (the Development queue is 1,024). |
| `results-day.js` | `setup()` logs in a pool of `LOGIN_POOL` (default 200) students spread across the number range; each iteration picks a random session and calls `GET /api/me/dashboard`. Check `dashboard 200`; `results_visible` is a `Rate` metric (informational: 1 on the demo now that the seeded instant has passed), not a threshold. Thresholds unchanged: `http_req_failed{endpoint:dashboard}: rate<0.01`, `http_req_duration{endpoint:dashboard}: p(95)<500, p(99)<1000`. |
| `dashboard-knee.js` | Same pool; adds counter `shed_503` and `rate_limited_429`; `summaryTrendStats` unchanged. The finding is "excess is shed fast" rather than "nothing fails". |
| `login-storm.js` (new) | Two modes via `-e MODE=guard` (default) and `-e MODE=spray`. **guard**: ramping arrival rate 5 → 40 logins/s over 2 minutes against `POST /api/auth/login` with a fresh csrf per iteration and a **distinct username per iteration** (`'S' + String(1 + Math.floor(Math.random() * 20000)).padStart(6, '0')`, so the per-user window never trips); counts 200 / 401 / 429; records p95 per stage. Run against the production-strength CPU guard (section 9 step 5). **spray**: from one synthetic address (`X-Forwarded-For: 203.0.113.10`, honoured because Development trusts forwarded headers) sends wrong passwords for 500 distinct usernames, expects 429 after `LoginFailuresPerIpPer10Minutes` failures, then a correct login for `S000001` from `203.0.113.11` and asserts 200 (nobody was locked). Requires `RateLimiting__LoginFailuresPerIpPer10Minutes=20` for the run. |
| `load/results/runs.json` (new) | `[{ file, scenario, version: 'v0' \| 'v1', label, ranAt, targetRate?, mode?, notes }]` maintained by hand for every committed summary. |
| `load/summarize.mjs` (new) | Reads `runs.json` and each summary, writes `src/RushDay.Web/public/data/load-results.json` (`05-frontend.md` section 8); `p99Ms` is optional for **every** run and emitted only when `metrics.http_req_duration['p(99)']` exists (the committed v0 enrolment-rush and results-day summaries lack it); `--check` exits non-zero when the committed JSON differs (CI). |
| `scripts/load.ps1` | adds `login-storm`, `-Rushers`, `-LoginPool`, `-Mode` and a `-ProductionLoginGuard` switch that prints and sets `RateLimiting__LoginConcurrency=8 RateLimiting__LoginQueue=16 RateLimiting__LoginPerIpPerMinute=100000 RateLimiting__LoginPerUserPerMinute=100000 RateLimiting__LoginFailuresPerIpPer10Minutes=100000` for the API it launches through `scripts/run-api.ps1`; unchanged file naming. |
| `load/README.md` | updated for authentication, the relaxed Development limits, `run-api.ps1`, the two login-storm modes and their environment variables. |

Development `appsettings.Development.json` carries the `RateLimiting` values from `03-security.md` section 8 and
`Database:MaxPoolSize = 40`; load runs use `scripts/run-api.ps1` (Release publish, `ASPNETCORE_ENVIRONMENT=Development`).

## 9. Capturing before/after evidence

Procedure (stage S13 of `06-implementation-plan.md`), all on the same laptop as the baseline, PostgreSQL native. The
API is always started with `scripts/run-api.ps1` (never `dotnet run`).

1. `scripts/reset-db.ps1` (fresh seed, backfills, demo accounts); `scripts/run-api.ps1`.
2. `scripts/load.ps1 enrolment-rush` → expect `accepted=30`, `OVERSOLD=0`, `enrolments_rejected_full=470`,
   `enrolments_shed=0`, `enrolments_errored=0`; record the rejections' latency (average, p95, p99) as measured.
   Record the API log grep for `53300` and `40P01` (both must be empty). Reset and repeat once to show repeatability.
   Every scenario signs its students in during `setup()` spread over time (the `loginMany` batches), never with every
   VU opening its first connection in the same instant: on this Windows client edition the listen backlog is capped
   below Kestrel's `Backlog = 1024`, and in the S4 review 16 of 250 and 90 of 300 simultaneous first connections were
   refused (`actively refused`) while logins spread over 15 s had none and the enrolment phase itself never had one.
   The evidence document reports the losers' measured latency rather than a "sub-10 ms" claim: the S4 review measured
   about 230 ms on average (p95 281 ms) for the 270 losers of a 300-student rush on the laptop, from queueing and CPU,
   not locking.
3. `scripts/load.ps1 results-day` → thresholds pass; note `rushday.dashboard.queries` from `/api/admin/ops/metrics`
   (`queriesPerRequest = 5`) and `process.workingSetBytes`.
4. `scripts/load.ps1 dashboard-knee -Rate 1000`, `2000`, `3000`, `4000` → table of achieved rate, p50/p95/p99,
   `shed_503`, `dropped_iterations`; API log grep for `53300` empty.
5. `scripts/load.ps1 login-storm -ProductionLoginGuard` (restarts the API with
   `RateLimiting__LoginConcurrency=8 RateLimiting__LoginQueue=16 RateLimiting__LoginPerIpPerMinute=100000
   RateLimiting__LoginPerUserPerMinute=100000 RateLimiting__LoginFailuresPerIpPer10Minutes=100000`, i.e. only the CPU
   guard at production strength) → 200/401/429 counts and p95 at each stage; then `scripts/load.ps1 login-storm -Mode spray`
   with `RateLimiting__LoginFailuresPerIpPer10Minutes=20` → the 429 onset and the successful login from the second
   address.
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
| 0007 | Identity with an EF Core store, cookie sessions over JWT, and a TOTP second factor for staff (Context opens with the story sentence pair) |
| 0008 | Atomic conditional update for module capacity |
| 0009 | Every route under `/api`; removal of the unauthenticated routes and the interim page |
| 0010 | Pool sizing, bounded waits and concurrency-limiter load shedding |
| 0011 | Set-based dashboard queries, academic years on enrolments, and in-process caching |
| 0012 | Results publication as a stored instant; set-based demo account backfill with a shared hash and the demo-off disable |

ADR 0003 gains a note that a native PostgreSQL install is an equal alternative to Docker and that the integration
tests support both (`RUSHDAY_TEST_CONNECTION`); ADR 0006 gains a note that Render deploys on `checksPass`, that the
Blueprint carries no demo values, and that the application role is not the database owner.
