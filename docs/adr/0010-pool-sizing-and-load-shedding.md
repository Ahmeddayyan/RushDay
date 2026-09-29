# 10. Pool sizing, bounded waits, and concurrency-limiter load shedding

Date: 2026-09-29

## Status

Accepted

## Context

`docs/load-results/2026-09-27-v0-baseline.md` shows the same symptom twice. Run 1 (the enrolment rush) logged
Postgres error `53300: remaining connection slots are reserved for roles with the SUPERUSER attribute` — the Npgsql
pool's default `Maximum Pool Size=100` is larger than what a default `max_connections=100` Postgres will actually
grant once its own reserved superuser slots are subtracted, so the pool could legitimately ask for more connections
than the server would ever give it. Run 3 (`dashboard-knee.js`, holding a fixed arrival rate for 30 seconds) found
the same error 417 more times under sustained saturation, plus a sharp knee between 800 and 1,000 requests/second
where p95 latency jumped from 6.5 ms to 2.5 seconds and throughput hit a ceiling of roughly 700-900 requests/second
*regardless of how much load was offered* — past the knee, offering more load made things worse, because the server
spent its time on requests that would eventually time out anyway rather than answering the ones it could.

The free-tier target makes this concrete: Render's web service gets 0.1 vCPU and 512 MB, and Neon's free compute
grants on the order of 112 total connections across every client that might be connected to it at once (not just
this application).

## Decision

- **Pool**: Npgsql `Maximum Pool Size=20`, `Minimum Pool Size=0`, connection `Timeout=5`, `Command Timeout=10` in
  Production (`Database:MaxPoolSize=40` locally, so a 500-VU load run on a laptop is not artificially pool-bound
  when the point of the run is to find a different limit). Twenty leaves headroom under Neon's grant with margin for
  Neon's own housekeeping connections and for a second instance briefly overlapping during a Render deploy.
- **Fail fast, not queue on Postgres**: a connection-pool wait that exceeds its timeout raises a exception the API
  maps to 503 `server-busy` immediately, rather than letting a request sit blocked on a database connection nobody
  is coming to free. Command timeout is 10 seconds on the request path (startup statements get a separate, much
  longer `Database:StartupCommandTimeoutSeconds=600`, because a migration or the results backfill legitimately takes
  longer than any request should ever wait).
- **Shed excess before it reaches the database at all**: a global ASP.NET concurrency limiter sits in front of every
  `/api` route except `/api/health/live` and `/api/admin/ops/metrics` (which must answer even while everything else
  is being shed, so Render's health check and the ops page itself never go dark): 24 permits and a 96-request queue
  in Production, sized well under Neon's connection grant, answering 503 with a `Retry-After` header the instant the
  queue is full. Locally the limiter opens up to 64 permits / 1,024 queued specifically so a 500-VU load run is
  never shed by the *limiter* when the experiment is about finding a *different* knee.
- Every rejection increments a metric (`rushday.load_shed.rejected{policy}`) so the ops page can show shedding as a
  plain-language line rather than a silent latency spike.

## Consequences

- The knee moves from "an unpredictable multi-second failure that keeps getting worse" (v0: p95 climbing to 2.5-8
  seconds while still offering low single-digit error rates) to "a fast, explicit, bounded rejection" (v1's target:
  excess requests answered with 503 and `Retry-After` inside one second). This is a deliberate trade: v1 fails a
  larger share of requests sooner in exchange for the requests it does accept staying fast, instead of everything
  degrading together.
- Twenty connections is a hard ceiling on database-bound throughput regardless of how many concurrency-limiter
  permits are granted; the two numbers are tuned together, not independently, and both are environment-specific
  (Production vs. local) for exactly this reason.
- `/api/health/live` and `/api/admin/ops/metrics` bypass the limiter on purpose. Render's own health check must
  never queue behind application traffic (a queued health check looks like a dead container and triggers a restart
  that cannot fix a database problem), and an operator needs the ops page to work precisely when everything else is
  saturated.

**v0 evidence:** `docs/load-results/2026-09-27-v0-baseline.md` runs 1 and 3 (53300 pool exhaustion; the 800-to-1,000
requests/second knee; the throughput ceiling under sustained saturation); raw summaries
`load/results/dashboard-knee-{1000,2000,3000,4000}rps-20260927-201844.json`.

**v1 evidence (stage S13):** `load/k6/dashboard-knee.js` at 1,000/2,000/3,000/4,000 requests/second — target: 503
with `Retry-After` inside a second for the excess, zero `53300` in the API log at any rate
(`04-performance-and-ops.md` section 9 step 4). Not yet recorded; filled in by
`docs/load-results/2026-10-xx-v1-hardened.md` and `load/results/dashboard-knee-*.json`.
