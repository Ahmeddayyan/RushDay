# 11. Set-based dashboard queries, academic years on enrolments, and in-process caching

Date: 2026-09-29

## Status

Accepted

## Context

v0's dashboard did roughly 15 queries per request: one for the student, one for their enrolments, then one module
query and one timetable query *per enrolment*, then grades, then one module query *per grade*. `docs/load-results/2026-09-27-v0-baseline.md`
run 2 found this did **not** collapse the dashboard everyone predicted would fail first — 0% `http_req_failed` at
800 requests/second, p95 6.5 ms — because with PostgreSQL on the same laptop as the API, each of those 15 queries
costs a fraction of a millisecond. The N+1 pattern is real but its cost is proportional to network latency between
the API and the database, which is close to zero on one machine and is exactly not zero for Neon in Frankfurt talking
to a Render container that may be in a different AWS region. A query-count fix has to be made on principle, ahead of
having the latency to prove it costs anything locally (`04-performance-and-ops.md` section 9 step 6 plans the
injected-latency experiment that will make the *old* pattern's cost visible against the same database).

Separately, v1 adds a second academic year of live data on top of the seeded 2025/26 enrolments (D28): without an
explicit year on every enrolment, "this year's modules" has no definition, a student's credit budget looks
permanently full from an old year's rows, and a results publication for one (year, semester) would have no way to
avoid touching an unrelated year's marks.

## Decision

- **Five set-based queries, not fifteen-per-module**: student; every year's active enrolments joined to modules in
  one round trip, partitioned in memory into "current" and "completed"; this year's current-semester timetable
  slots for those modules; visible grades joined to modules and enrolments; announcements. Each is `AsNoTracking`,
  projects straight to a record (no entity materialisation), and its query count is measured by an EF command
  interceptor (`DbCommandCounter`) rather than asserted by hand-counting in a test, so a future change that
  reintroduces an N+1 fails the build automatically instead of waiting for someone to notice.
- **`enrolments.academic_year`** (D28) on every row, stamped from `academic_settings` at enrolment time (and
  re-stamped on reactivation). "Current modules" is the active enrolments whose year matches the settings row;
  everything earlier is "completed". `modules.enrolled_count` only counts the current year's active rows, so last
  year's cohort never occupies this year's places and changing the settings year recomputes the count rather than
  silently going stale.
- **In-process caching, no Redis** (D13, D14): the module catalogue (`GET /api/modules`, viewer-agnostic) is cached
  30 seconds with `HybridCache` and no L2 store; the enrolment window calendar, academic settings and the current
  results publication are cached similarly. A viewer's own state (`GET /api/me/enrolments`) and a single module's
  detail page are read uncached, so a rush of catalogue reads costs zero database work while the numbers that must
  never be stale (a specific enrolment decision, a specific module's live count) never come from a cache.

## Consequences

- Query count is now a constant regardless of how many modules or grades a student has, which is the actual fix for
  N+1 (a smaller constant multiplied by rows is still not constant); a timetable-specific cache was considered and
  rejected because it would add invalidation complexity for a query that is already O(1) round trips.
- `academic_year` on enrolments is a one-way door: every staff read of enrolments and grades, and every publication,
  is now year-scoped by construction, which is what makes a second academic year of live data (the demo's 2026/27
  enrolment windows on top of 2025/26's published results) behave correctly instead of merging two years' state.
- Cache staleness is bounded (30 seconds) and scoped to data that changing it by 30 seconds does not matter for
  (an inactive module briefly still listed, a settings change taking half a minute to show up on the catalogue); it
  is never used for a number a decision is made against (capacity checks, credit limits and window checks all read
  live).

**v0 evidence:** `docs/load-results/2026-09-27-v0-baseline.md` run 2 (0% failure, p95 6.5 ms at 800 requests/second
locally — the finding that the N+1 pattern's cost needs network latency to show, not more load on a local database);
raw summary `load/results/results-day-20260927-201336.json`.

**v1 evidence (stage S13):** `load/k6/results-day.js` at 800 requests/second — target: `http_req_failed{endpoint:dashboard}`
< 1%, p95 < 500 ms, and `rushday.dashboard.queries = 5` read from `/api/admin/ops/metrics` (down from v0's ~15); the
injected-latency experiment against `v0-naive` and v1 (`04-performance-and-ops.md` section 9 step 6, Should) if time
allows. Not yet recorded; filled in by `docs/load-results/2026-10-xx-v1-hardened.md` and
`load/results/results-day-*.json`.
