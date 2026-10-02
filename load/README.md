# Load tests

Four k6 scenarios, one per story. v0's scripts hit bare, unauthenticated routes
(`/students/{n}/dashboard`, `/students/{n}/enrolments`); v1 removed those routes (D25), so every scenario now signs
in for real through `load/k6/lib/auth.js`: `GET /api/auth/csrf` → `POST /api/auth/login` with the CSRF header →
a session cookie and a fresh token. Logins happen in a scenario's `setup()`, spread across `http.batch` chunks of 25,
not fired as one instant of simultaneous connects — see "A Windows limit worth knowing about" below for why that
matters on this machine.

| Script | Story | What to watch |
|---|---|---|
| `k6/results-day.js` | 09:00, marks published, a cohort opens the dashboard | `http_req_failed{endpoint:dashboard}`, p95/p99, `rushday.dashboard.queries` on `/api/admin/ops/metrics` |
| `k6/enrolment-rush.js` | Students race for 30 places on one module | The `OVERSOLD=` line in teardown; `enrolments_accepted`/`enrolments_rejected_full`/`enrolments_shed`/`enrolments_errored` |
| `k6/dashboard-knee.js` | Hold the dashboard at a fixed rate (`-e RATE=2000`) for 30s | Where p95 climbs, `shed_503`, `rate_limited_429`, `dropped_iterations` |
| `k6/login-storm.js` | The login endpoint itself, guard and spray modes (`-e MODE=guard\|spray`) | Guard: 200/401/429 counts and p95 per ramp phase, the PBKDF2 concurrency guard's throughput. Spray: the 429 onset from one address, and that a correct login from a second address still succeeds |

## Run

The API is always started through `scripts/run-api.ps1` (never `dotnet run`); `scripts/load.ps1` does this for you.

```powershell
.\scripts\seed.ps1                       # once: migrate + seed 20,000 students
.\scripts\load.ps1 results-day
.\scripts\load.ps1 enrolment-rush -Rushers 500
.\scripts\load.ps1 dashboard-knee -Rate 2000
.\scripts\load.ps1 login-storm -Mode guard -ProductionLoginGuard
.\scripts\load.ps1 login-storm -Mode spray
.\scripts\reset-db.ps1                   # between enrolment-rush runs, for a clean 30 places
```

`scripts/load.ps1` publishes and starts its own API, waits for `/api/health/live`, runs the scenario, writes a JSON
summary to `load/results/`, and stops the API again. If you already have an API running (your own
`scripts/run-api.ps1` in another window, or a scratch instance on a non-default port and database), point at it
instead and skip the built-in start/stop:

```powershell
.\scripts\load.ps1 results-day -BaseUrl http://localhost:5101   # a different port/instance
.\scripts\load.ps1 dashboard-knee -Rate 1000 -NoStart           # http://localhost:5080, already running
```

Run the API in Release when measuring; a `dotnet build -c Debug` publish is noticeably slower. Do not point any of
this at the free-tier deployment; it is shared hardware and the numbers would mean nothing (see the README's
"Findings so far" section and `docs/load-results/`).

### `login-storm.js`'s two modes

`login-storm` restarts the login limiters, not the whole application: it is the same published API, started with
extra environment variables for the duration of the run.

- **`-Mode guard -ProductionLoginGuard`**: sets `RateLimiting__LoginConcurrency=8`, `RateLimiting__LoginQueue=16`
  (the production defaults for the PBKDF2 concurrency guard, D6) and opens every *other* login limiter up to
  100,000/minute, so the run measures the guard alone: how many real logins per second 210,000-iteration PBKDF2 on
  0.1 vCPU can sustain before the guard's queue overflows and answers 429. Without `-ProductionLoginGuard` the
  Development limits apply instead (`appsettings.Development.json`, `LoginConcurrency=64`), which is a fine smoke
  check but not the number the acceptance checklist asks for.
- **`-Mode spray`**: sets `RateLimiting__LoginFailuresPerIpPer10Minutes=20` (the production default; Development
  relaxes it to 100000 so hundreds of k6 logins do not trip it by accident) and sends wrong passwords for 500
  distinct usernames from one synthetic address, then a correct login for `S000001` from a second address. The
  story is D6: a single address gets throttled by the per-IP failed-login window well before Identity's lockout
  (5 failures from **3 or more** addresses) would ever trigger, so the victim's own account is never locked by an
  attacker hitting it from one place.

### Passwords and student numbers

Every script defaults to the demo student password `Student-Demo-2026!` (`-e PASSWORD=...` to override) and to
`STUDENT_COUNT=20000` (the full local seed; pass `-e STUDENT_COUNT=300` if you are pointing at a smaller database,
for example an integration-test-sized clone). `enrolment-rush.js` signs in `S000001..S00000{RUSHERS}`
consecutively (it needs a fixed, known cohort so the module's capacity is the only thing being contested);
`results-day.js`, `dashboard-knee.js` and `login-storm.js -Mode guard` pick student numbers spread across the whole
range or at random, so the dashboard's caches are not warmed by hitting the same few rows.

### A Windows limit worth knowing about

When every k6 virtual user tries to connect in the same instant (250-300 simultaneous first connections in one
review run), a share of them got "connection refused" at the TCP level before Kestrel ever saw them, even with
Kestrel's listen backlog at 1024 — most likely the Windows *client-edition* listen backlog cap, which is lower than
the server editions' and is not something this application can raise from inside .NET. Spreading the same logins
over roughly 15 seconds (which `loginMany`'s batching does by construction) brought the refusal rate to zero. This
is a limit of running the load generator and the API on the same Windows 11 Home laptop, not a server-side bug, and
it is why every v1 scenario signs its users in during `setup()` in batches rather than as one simultaneous storm —
the measured window starts only once everyone already holds a session. `docs/load-results/` reports this plainly
rather than describing an artificially smoothed-over number.

## Files

- `k6/lib/auth.js` — the session helpers every scenario imports (`csrf`, `login`, `loginMany`, `params`).
- `k6/enrolment-rush.js`, `k6/results-day.js`, `k6/dashboard-knee.js`, `k6/login-storm.js` — the scenarios.
- `results/` — one JSON summary per run (`k6 run --summary-export`), named `<scenario>-<timestamp>.json`, committed
  as evidence. `results/runs.json` labels each committed summary for `summarize.mjs`.
- `summarize.mjs` — reads `results/runs.json` and each summary, writes
  `src/RushDay.Web/public/data/load-results.json`, which the public `/story` page and the admin ops page render
  (`--check` exits non-zero when the committed JSON is stale; run in CI).

See `docs/load-results/2026-09-27-v0-baseline.md` for the v0 numbers and `docs/load-results/2026-10-02-v1-hardened.md`
(written in stage S13) for the v1 comparison.
