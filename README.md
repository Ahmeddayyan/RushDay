# RushDay

[![CI](https://github.com/Ahmeddayyan/RushDay/actions/workflows/ci.yml/badge.svg)](https://github.com/Ahmeddayyan/RushDay/actions/workflows/ci.yml)

I built RushDay because my own university's portal fell over every results day and enrolment window. I wanted to
understand why that happens and to build a portal that doesn't crash under the same load.

RushDay is a university student portal: students see marks the moment they are published, enrol on modules while
places last, withdraw before a deadline and read announcements; lecturers see rosters, enter and submit marks, and
post module announcements; administrators run enrolment windows, publish results at a chosen instant, correct and
unpublish when the exam board says so, fix enrolments with a reason, provision accounts, and watch the system under
load in plain language. Everything that changes a mark, an enrolment or an account is audited in the same
transaction as the change, and the database itself rejects any attempt to edit or delete an audit row.

The build is deliberately in two acts. **v0** is a naive, straightforward implementation — check-then-write
enrolment, one query per module on the dashboard, no auth, no caching — load-tested to find out exactly how and why
it breaks. **v1** is the same product rebuilt with authentication, authorization, an atomic capacity fix, set-based
queries, in-process caching, connection-pool sizing and load shedding, and a full academic and security model,
with every fix backed by an ADR and a before/after load run. Nothing here is optimised speculatively: if it isn't
measured, it isn't claimed.

**Live demo:** **https://rushday-api.onrender.com** — Render free web service (Docker) + Neon free PostgreSQL. Free
tier spins down when idle; the first request after a pause can take 30-60 seconds (the login page says so).

**Repository:** https://github.com/Ahmeddayyan/RushDay · **Owner:** Ahmed Ayyan

## What this project demonstrates

- **Concurrency control you can check.** Enrolment claims a place with one conditional `UPDATE ... WHERE
  enrolled_count < capacity`; an integration test fires 200 parallel enrolments at a 30-place module and expects
  exactly 30 successes, and authenticated k6 rushes of 300 students run against the full 20,000-student dataset.
- **Measure, then fix.** v0 was load-tested first; each v1 fix answers a measured failure and has its own ADR.
- **Security as a design input.** Cookie sessions with antiforgery, mandatory TOTP for administrators, role and
  module-level authorization enforced in the queries themselves, rate limiting, strict security headers, an encrypted
  key ring, and an append-only audit trail.
- **Adversarial review as part of the process.** Each backend stage was reviewed by an independent reviewer trying
  to break it (sessions that outlived a disabled account, a deadlock between administrative enrolments, a mark that
  could be stranded by a re-enrolment); every blocker and major finding was fixed with a regression test that failed
  before the fix, and the reasoning for the rest is recorded in the spec.
- **A real product, not a load-test harness.** Three roles, the full results lifecycle (draft, submit, publish at an
  instant, reschedule, cancel, unpublish, correct), accounts and provisioning, an ops page, WCAG 2.2 AA as the
  accessibility target, and a public page that tells this story with the numbers.

## Demo accounts

Shown on the live login page whenever demo mode is on (it is, on the public demo; a customer deployment starts with
none of these — see `docs/deployment.md`).

| Role | Username | Password |
|---|---|---|
| Student | `S000001` | `Student-Demo-2026!` |
| Lecturer | `L00001` | `Lecturer-Demo-2026!` |
| Administrator | `admin` | `Admin-Demo-2026!` |

The administrator account is exempt from the mandatory TOTP second factor only in demo mode, so you can see the
admin surface without setting up an authenticator app first; a real deployment's administrator always sets one up.

## Architecture

One container, one URL: an ASP.NET Core 10 minimal API serves a React 19 SPA straight out of `wwwroot`, backed by one
PostgreSQL database (Neon in the cloud, PostgreSQL 18 locally). Every API route lives under `/api`; everything else
is the SPA and its client-side routing.

| Concern | Choice | Why (ADR) |
|---|---|---|
| Runtime | .NET 10, ASP.NET Core minimal APIs | [ADR 2](docs/adr/0002-dotnet-10-minimal-apis.md) |
| Data | PostgreSQL 18, EF Core 10, Npgsql | [ADR 3](docs/adr/0003-postgresql-in-docker.md) |
| Auth | ASP.NET Core Identity, cookie sessions, TOTP for staff | [ADR 7](docs/adr/0007-identity-and-cookie-sessions.md) |
| Enrolment concurrency | One atomic conditional `UPDATE` | [ADR 8](docs/adr/0008-atomic-capacity-update.md) |
| Routing | Everything under `/api`; no unauthenticated route survives from v0 | [ADR 9](docs/adr/0009-everything-under-api.md) |
| Pool, timeouts, shedding | Sized pool, fast 503s over queueing on Postgres | [ADR 10](docs/adr/0010-pool-sizing-and-load-shedding.md) |
| Dashboard queries | Five set-based queries, in-process caching | [ADR 11](docs/adr/0011-set-based-queries-and-caching.md) |
| Results publication | A stored instant, no scheduler | [ADR 12](docs/adr/0012-stored-publication-instant-and-demo-backfill.md) |
| Front end | React 19, TypeScript, Vite, Tailwind CSS 4, TanStack Query | [docs/spec/05-frontend.md](docs/spec/05-frontend.md) |
| Tests | xUnit; Testcontainers in CI, native PostgreSQL locally | [ADR 3](docs/adr/0003-postgresql-in-docker.md) update |
| Load | Grafana k6, authenticated scenarios | [ADR 4](docs/adr/0004-k6-for-load-testing.md) |
| Hosting | Render (API, Docker, `checksPass` gating) + Neon (PostgreSQL) | [ADR 6](docs/adr/0006-free-hosting-render-and-neon.md) |

```
docs/spec                 the specification: the single source of truth for v1 (start at 00-overview.md)
docs/adr                  architecture decision records: the reasoning behind each major decision
docs/load-results         what each load run showed, with numbers
docs/deployment.md        every configuration variable, the customer deployment path, key rotation, backup/restore
docs/admin-guide.md       running the product day to day: windows, publishing, overrides, accounts, the demo switch
src/RushDay.Domain        entities and pure rules (no I/O)
src/RushDay.Infrastructure EF Core, migrations, the idempotent startup backfills
src/RushDay.Api           endpoints, contracts, security, observability
src/RushDay.Web           the React SPA (built into src/RushDay.Api/wwwroot)
tests/                    unit + integration tests
load/k6                   authenticated load scenarios; load/results holds run summaries
scripts/                  run-api, db-create, seed, reset-db, load, check-story
```

## Run it locally

Prerequisites: .NET 10 SDK, Node 24, k6, and a PostgreSQL 18 reachable at `localhost:5432` (a native install or
Docker both work).

Run the API with `scripts/run-api.ps1`: it publishes a framework-dependent single-file build and starts it on
http://localhost:5080. It is the one supported way to run the API locally, and it also works on Windows machines
where Smart App Control blocks `dotnet run` on freshly built, unsigned assemblies.

```powershell
.\scripts\db-create.ps1                              # creates the rushday role and database (native PostgreSQL)
.\scripts\seed.ps1                                   # migrate + seed 20,000 students (skips if already seeded)
.\scripts\run-api.ps1                                # http://localhost:5080
```

The front end lives in `src/RushDay.Web` (Vite + React + TypeScript + Tailwind CSS 4; needs Node 24). The API serves
its production build from `src/RushDay.Api/wwwroot`, so a built front end and the API together are one process and
one URL:

```powershell
cd src\RushDay.Web
npm install
npm run dev          # Vite dev server on http://localhost:5173, proxying /api to :5080 for day-to-day front-end work
npm run build        # writes src\RushDay.Api\wwwroot; scripts\run-api.ps1 then serves it at http://localhost:5080
```

Open http://localhost:5080 for the app, or walk the API directly with `src/RushDay.Api/RushDay.Api.http` (every
route, with the session flow: csrf → login → me). A quick anonymous check once the API is running:

```
GET  /api                                JSON index: the story, links, the deployed commit
GET  /api/health/live                    Render's health check
GET  /api/public/status                  demo accounts (when demo mode is on) and the latest results publication
```

Everything else needs a session — see `RushDay.Api.http` for the full login → action flow for each role.

## Tests

```powershell
dotnet test tests\RushDay.UnitTests

# Integration tests use Testcontainers (Docker) by default, as in CI. Without Docker, point them at a native
# PostgreSQL instead; the test factory creates and drops its own throwaway database:
$env:RUSHDAY_TEST_CONNECTION = "Host=localhost;Port=5432;Database=rushday_test;Username=rushday;Password=rushday"
dotnet test tests\RushDay.IntegrationTests

cd src\RushDay.Web
npm test              # Vitest component tests (jsdom)
npm run test:e2e      # Playwright journeys and accessibility scans against a running API
```

`dotnet build RushDay.slnx -c Release` and `npm run lint && npm run typecheck && npm run build` (in
`src/RushDay.Web`) are both zero-warning gates in CI (`.github/workflows/ci.yml`); warnings are errors throughout.

## Load testing

Every v1 scenario authenticates for real (v0's bare, unauthenticated routes are gone — [ADR 9](docs/adr/0009-everything-under-api.md)).

```powershell
.\scripts\load.ps1 results-day
.\scripts\load.ps1 enrolment-rush -Rushers 500
.\scripts\load.ps1 dashboard-knee -Rate 2000
.\scripts\load.ps1 login-storm -Mode guard -ProductionLoginGuard
.\scripts\load.ps1 login-storm -Mode spray
.\scripts\reset-db.ps1                               # between enrolment-rush runs, for a clean 30 places
```

`scripts/load.ps1` starts and stops its own API (or point it at one you already have running with `-BaseUrl`/
`-NoStart`). See [load/README.md](load/README.md) for what each scenario measures, the two `login-storm` modes, and
a Windows-specific TCP-refusal limit worth knowing about before reading too much into a raw error count. Summaries
land in `load/results/` and are committed as evidence; `load/summarize.mjs` turns them into the JSON the public
[`/story`](https://rushday-api.onrender.com/story) page and the admin ops page render. Never point a load run at the
free-tier deployment — it is shared hardware and the numbers would mean nothing.

## Findings so far

### v0 baseline (27 September 2026)

Full write-up: [docs/load-results/2026-09-27-v0-baseline.md](docs/load-results/2026-09-27-v0-baseline.md).

- **Enrolment rush**: 500 students hit a 30-place module at once. **154 got in** (124 oversold) — a check-then-write
  race with no isolation between the read and the write. The database also ran out of connection slots
  (Postgres `53300`) under the resulting retry storm, and 260 of 500 connections were refused at the TCP level
  before the app ever saw them.
- **Results day**: the dashboard I expected to fall over first handled 800 requests/second with a p95 of
  6.5 ms and zero errors — its ~15-query-per-request N+1 pattern is cheap when the database is on the same machine.
  Finding its real cost needed a different experiment (see the injected-latency plan in
  [`docs/spec/04-performance-and-ops.md`](docs/spec/04-performance-and-ops.md) section 9).
- **Where the dashboard actually breaks**: holding a fixed request rate found a sharp knee between 800 and 1,000
  requests/second, where p95 latency jumped from 6.5 ms to 2.5 seconds, and a hard throughput ceiling of roughly
  700-900 requests/second that offering more load made *worse*, not better.

### v1 (not yet measured)

v1's fixes are built and tested (the acceptance checklist in
[`docs/spec/00-overview.md`](docs/spec/00-overview.md) section 8 tracks each one against an integration test), but
the before/after load numbers this section will quote once the release evidence run follows the procedure in
`docs/spec/04-performance-and-ops.md` section 9 are **not yet recorded**. Expect, and do not yet cite as fact:
zero oversold places on the enrolment rush, `rushday.dashboard.queries = 5` (down from v0's ~15), 503s with
`Retry-After` replacing multi-second failures beyond the knee, and the login endpoint's own throughput under its
concurrency guard. The dated write-up will land at `docs/load-results/2026-10-xx-v1-hardened.md`, linked from every
ADR above.

## Specification and decisions

The specification (`docs/spec/00` through `06`) is the single source of truth for v1 — where any other document
disagrees with it, the spec wins and the other document gets fixed. Start at
[`docs/spec/00-overview.md`](docs/spec/00-overview.md) for the story, personas, scope and the decision register;
[`docs/adr/`](docs/adr/) records the reasoning behind each major decision, and the performance-related ones link the
load evidence that motivated them. [`docs/deployment.md`](docs/deployment.md) and [`docs/admin-guide.md`](docs/admin-guide.md) are the
operational side: how to deploy this for your own institution, and how to run it day to day once it's live.
