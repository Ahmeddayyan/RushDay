# RushDay v1 specification: 00. Overview

Status: accepted. Written 2026-09-27 against commit `942b5ac` (plus the uncommitted `src/RushDay.Web` scaffold).
This folder (`docs/spec/00` to `06`) is the single source of truth for v1. Where an ADR, README or design note
disagrees with these files, these files win and the other document is updated.

The seven files:

| File | What it fixes |
|---|---|
| `00-overview.md` | The story, personas, academic processes, scope (Must/Should/Could/Won't), the decision register, and the acceptance checklist for "production-ready". |
| `01-domain-and-data.md` | Entities, tables, columns, constraints, indexes, the one migration, the idempotent startup backfills for the populated Neon database, demo accounts. |
| `02-api.md` | Every route under `/api` with request and response JSON, status codes, role requirements; session, antiforgery, rate-limit policies, ProblemDetails. |
| `03-security.md` | Threat model, controls, exact headers and CSP, configuration and secrets per environment, logging and audit rules, what is out of scope. |
| `04-performance-and-ops.md` | The enrolment concurrency fix, query shapes, caching, pool and timeouts, load shedding, metrics, health, the k6 changes and how before/after evidence is captured. |
| `05-frontend.md` | Stack, structure, routing, auth flow, data layer, design system, page-by-page spec, accessibility, tests, build integration. |
| `06-implementation-plan.md` | Ordered stages with file ownership, definition of done and local verification (no Docker). |

## 1. The story

This sentence pair is the owner's story and appears **verbatim** wherever the project describes itself:

> I built RushDay because my own university's portal fell over every results day and enrolment window. I wanted to understand why that happens and to build a portal that doesn't crash under the same load.

Required locations (each is a checklist item in section 8):

1. `README.md`, first paragraph after the title.
2. `GET /api` index, the `story` field.
3. The login page strapline (`/login`) and the opening of the public `/story` page.
4. `src/RushDay.Web/index.html` `<meta name="description">`.
5. `docs/adr/0007-identity-and-cookie-sessions.md`, Context section (the first v1 ADR).

The measured narrative stays central: v0 was deliberately naive (ADR 5), the baseline broke in three ways
(`docs/load-results/2026-09-27-v0-baseline.md`: 154 places handed out for 30, Postgres error 53300 pool
exhaustion, 260 TCP refusals; dashboard knee between 800 and 1,000 rps), and v1 fixes each with an ADR and a
before/after run. v1 also turns RushDay into something a university could buy: three roles, real academic
processes, authentication and authorization, security by default, an ops view, and evidence.

## 2. What RushDay v1 is

A single container (ASP.NET Core 10 minimal API serving a React 19 SPA from `wwwroot`) backed by one PostgreSQL
database (Neon free tier in the cloud, PostgreSQL 18 locally). Students see marks the moment they are published,
enrol on modules while places last, withdraw before a deadline and read announcements. Lecturers see rosters,
enter and submit marks, and post module announcements. Administrators run enrolment windows, publish results at a
chosen instant, fix enrolments with a reason, provision accounts, and watch the system. Everything a person does
that changes marks, enrolments or accounts is audited.

Hard constraints (from the brief, restated so no stage relaxes them):

- .NET 10 backend, EF Core 10 + Npgsql + PostgreSQL. React 19 + TypeScript + Vite + Tailwind CSS 4 front end built
  into `src/RushDay.Api/wwwroot`. One container, one URL. All API routes under `/api`.
- Free tier survives: Render web service (0.1 CPU, 512 MB, one instance) + Neon PostgreSQL. No Redis, no paid
  add-ons, in-process caching only, memory-conscious.
- The Neon database is already seeded with the 20,000-student dataset. Every schema change is an EF migration that
  works on a populated database; every data backfill is an idempotent startup step.
- No self-registration. The university provisions accounts. Demo accounts (one per role) are shown on the login page
  only when demo mode is on.
- The k6 scenarios keep working, so they authenticate.
- No Docker locally: integration tests run against Testcontainers in CI and against the native PostgreSQL 18 locally
  (`RUSHDAY_TEST_CONNECTION`), see `06-implementation-plan.md`.

## 3. Personas

| Persona | In the data | Jobs to be done | What "the portal fell over" costs them |
|---|---|---|---|
| **Student** | One of 20,000 `students` rows (`S000001`..`S020000`), login = student number. | See my marks the moment they are published. Enrol on the modules I want before places go. Know my timetable. Withdraw before the deadline. Read announcements. | Refreshing a dead page at 09:00; losing a place on CS3099 to a request that happened to land; not knowing whether a click "worked". |
| **Lecturer** | One of 40 `lecturers` rows (`L00001`..`L00040`), assigned to modules through `module_lecturers`, login = staff number. | See who is on my modules. Enter marks as drafts, correct them, submit the module when complete. Post module announcements. Never show a student an unpublished mark. | Marks visible before the exam board signed off; spreadsheets emailed around; roster mismatches. |
| **Administrator** | `admin` account (registry / academic office), no student or lecturer record. | Open and close enrolment windows. Set the results-day instant and publish what lecturers submitted. Fix enrolments by hand with a written reason. Provision, lock, reset and disable accounts. See who did what. See that the system is healthy and how it behaved under load. | Being the person on the phone at 09:05 while the site is down; oversold modules nobody can untangle; no record of who changed a mark. |
| **University IT buyer** (not a login) | Evaluates the repository, the live demo and the checklist in section 8. | Prove it is secure, accessible, observable, recoverable, and that the load claims reproduce. | Buying another portal that dies on results day. |

## 4. Academic processes the product models

All time comparisons use `TimeProvider.GetUtcNow()` at request time. **There is no background scheduler.** Anything
that "happens at 09:00" is a stored instant that reads evaluate against; on a free-tier container that spins down
when idle a timer is a lie, a stored instant is deterministic, testable with a fake clock and survives restarts.

### 4.1 Academic year and enrolment windows

- One `academic_settings` row names the current `academic_year` (`"2026/27"` on the demo), the institution name and
  the display time zone (`Europe/London`).
- One `enrolment_windows` row per (`academic_year`, `semester`): `opens_at`, `closes_at`, `withdrawal_deadline_at`.
- A student may self-enrol on module M iff a window exists for (current year, M.semester) and
  `opens_at <= now < closes_at`. Self-withdrawal is allowed iff `now < withdrawal_deadline_at`.
- Outside a window the catalogue still shows every active module with `enrolmentState` = `notYetOpen | open | closed`
  and the instants, so students can plan.
- Demo data: Autumn 2026/27 window open 2026-09-14 09:00 to 2026-10-02 17:00 UTC (withdrawal deadline 2026-10-30
  17:00) and Spring 2026/27 open 2026-09-14 09:00 to 2027-01-29 17:00 UTC (withdrawal deadline 2027-02-26 17:00).
  Both are open on 2026-09-27; seeded students already hold 60 autumn credits, so autumn attempts exercise the credit
  rule and spring modules such as CS3099 are enrolable.

### 4.2 Module capacity

- Capacity is a hard invariant: `modules.enrolled_count <= modules.capacity` for every module at every instant, where
  `enrolled_count` is the number of `enrolments` rows with `status = 'Active'`. It is enforced by a single atomic
  conditional `UPDATE ... WHERE enrolled_count < capacity` inside the enrolment transaction (`04-performance-and-ops.md`
  section 2). No read-then-write path exists.
- v1 is first come, first served. When full, the student gets a fast 409 `module-full`; the catalogue shows "Full"
  with the live count; withdrawing frees a place immediately.
- Waiting lists are Should (section 5) because promotion is a second race with a notification requirement that v1 has
  no channel for.

### 4.3 Results: draft, submitted, published

Each (`student`, `module`) grade row has `status`:

```
Draft ──lecturer submits module──▶ Submitted ──admin publishes semester at publish_at──▶ Published
  ▲                                     │
  └──────── admin returns to draft ─────┘        (Published → Submitted: admin "unpublish", Should)
```

- **Draft**: the module's lecturers create and edit marks. Invisible to students. Visible to the module's lecturers
  and to administrators.
- **Submitted**: the lecturer declared the module complete; every active enrolment has a mark. Locked for lecturers.
  An administrator may return it to Draft with a reason.
- **Published**: an administrator publishes a semester: every Submitted grade on modules in that semester gets
  `status = 'Published'`, `published_at = publish_at`, `publication_id = <the results_publications row>`.
  `publish_at` may be the future (the 09:00 results-day moment) or the past (immediate).
- **The visibility rule that must be impossible to get wrong:** a student sees a grade iff
  `status = 'Published' AND published_at <= now`. This is one query filter (`GradeQueries.VisibleToStudents(db, now)`)
  that every student-facing read goes through. Student response contracts carry no field that could hold a draft
  mark. Before `published_at` the student sees "Autumn 2025/26 results publish on 28 Sep 2026 at 10:00 (Europe/London)".
- The 80,000 seeded grades become `Published` with their existing `published_at` (2026-09-28 09:00 UTC) and a
  seed `results_publications` row. The live demo therefore "goes live" with results at that instant, which is the
  story; the demo administrator can reschedule a publication that is not yet live.

### 4.4 Announcements

`scope = 'University'` (administrators) or `'Module'` (the module's lecturers and administrators). Plain text, rendered
with line breaks only. `published_at` may be in the future; optional `expires_at`; `pinned` sorts first.
Students see university announcements plus those of modules they are actively enrolled on; lecturers see university
plus their modules; administrators see all.

### 4.5 Timetable

Data unchanged (`timetable_slots`). Students see slots of active enrolments; lecturers see slots of their modules.
Timetable clash on enrolment is a Should **warning** in the UI, never a block: the seeded timetables are random and a
hard block would change the enrolment-rush numbers for the wrong reason.

## 5. Scope: MoSCoW per role (Must = buyable v1)

### Student

| Priority | Feature |
|---|---|
| Must | Sign in with student number; sign out; change password; forced password change on first login of a provisioned account. |
| Must | Dashboard: name, programme, year; active modules with credits; published results with credit-weighted average and classification; next results publication instant; enrolment window state and credit budget; announcements; the week's timetable with today first. |
| Must | Module catalogue with live `placesRemaining`, semester, credits, lecturers, window state, my enrolment state; enrol; clear reason on rejection (full, already enrolled, credit limit, window closed). |
| Must | Withdraw from a module before the withdrawal deadline (blocked when a submitted or published mark exists). |
| Must | Full weekly timetable; results page by semester with scheduled instants for unpublished semesters. |
| Should | Waiting list; timetable-clash warning; iCal export of the timetable; personal data export (JSON). |
| Could | Module prerequisites. |
| Won't (v1) | Self-registration; self-service password reset; fee payment; coursework submission; messaging. |

### Lecturer

| Priority | Feature |
|---|---|
| Must | Sign in with staff number; my modules with enrolled count, capacity and marks status (draft / submitted / published, entered / missing). |
| Must | Roster per module (student number, name, programme, year, enrolled at, status). |
| Must | Marks entry per module: editable grid of draft marks, save all (all-or-nothing upsert with optimistic `version`), who changed what and when; submit the module when complete, otherwise a 422 listing missing students. |
| Must | Read-only view of submitted and published marks for my modules. |
| Must | Post, edit and delete announcements on my modules. |
| Should | CSV export of the roster; CSV import of marks; my weekly teaching timetable. |
| Could | Per-student outcome notes (absent, deferred, mitigating circumstances). |

### Administrator

| Priority | Feature |
|---|---|
| Must | Enrolment windows: create, edit, delete per (academic year, semester). |
| Must | Results: submission progress per module for a semester; publish a semester at an instant (now or future, at most 90 days ahead); publication history; reschedule a publication that is not yet live; return a module to draft with a reason. |
| Must | Students: create a student; search by number or name; view a student as the student sees it plus statuses; override-enrol and override-withdraw with a mandatory reason (ignores windows and the credit limit; capacity applies unless `forceCapacity` raises capacity by one, audited). |
| Must | Modules: create and edit title, description, credits, capacity (never below `enrolled_count`), semester, active flag; assign lecturers (exactly one leader). |
| Must | Lecturers: create; list. |
| Must | Accounts: provision a user for an existing student or lecturer or a new administrator with a temporary password shown once; lock, unlock, disable, enable, reset password (all set "must change password"). |
| Must | University-wide announcements. |
| Must | Audit log filtered by actor, student, module, action, date range; CSV export. |
| Must | Ops page: live request rate, p95 and p99, error rate, load-shed and rate-limit rejections, DB pool usage and wait timeouts, memory; the load-results story with before/after charts; backfill status; data-quality warnings (modules over capacity, `enrolled_count` drift); a reconcile button. |
| Should | Unpublish a semester; bulk CSV import of students, lecturers and assignments; Microsoft Entra ID single sign-on through Identity external login (the first post-v1 request any UK university makes). |
| Could | Timetable slot editing; audit retention purge; academic-year rollover wizard. |

## 6. Decision register

Every disagreement between the three input designs is resolved here with one sentence of rationale. Later files
elaborate; they never reopen.

| # | Topic | Decision | Rationale |
|---|---|---|---|
| D1 | Identity store | ASP.NET Core Identity with the EF Core store, `ApplicationUser : IdentityUser<Guid>`, tables renamed to snake_case (`users`, `roles`, ...). No `MapIdentityApi`, no registration route. | Proven hashing, lockout and security stamps for free; registration is the only part we do not want. |
| D2 | Role names | `Student`, `Lecturer`, `Admin`; exactly one role per user. | Short names match the URL areas `/student`, `/lecturer`, `/admin`; one role per user removes precedence rules. |
| D3 | Lecturer identity | A domain `lecturers` table mirrors `students`; `users.lecturer_id` links them; `module_lecturers` references `lecturers`, not `users`. | Domain data must not depend on the login store, and a lecturer without a login is a valid record. |
| D4 | Session | Cookie authentication (`rushday.auth`, HttpOnly, Secure, SameSite=Strict, 8 h sliding, 12 h absolute), security stamp validated every 5 minutes. JWT rejected. | Same-origin SPA: an HttpOnly cookie is unreadable by script and revocable; a JWT needs script-readable storage and a denylist. |
| D5 | Antiforgery | Header `X-CSRF-TOKEN` on every non-GET `/api` request including login; the token is returned in the JSON of `GET /api/auth/csrf`, `POST /api/auth/login` and `GET /api/auth/me` (never as a readable cookie). | The SPA never parses cookies, the token rotates with the principal, and k6 follows the same two calls. |
| D6 | Login rate limiting | Keyed by normalised username (10/min) plus a login concurrency limiter (8 concurrent, 64 queued) and a very high per-IP backstop (600/min). Not per-IP as the primary key. | A campus NAT puts a hall of residence behind one address; per-IP limiting would lock them out at 08:59, and PBKDF2 CPU is the resource that actually needs protecting on 0.1 CPU. |
| D7 | Demo accounts | In demo mode all 20,000 students and all 40 lecturers get logins with one precomputed password hash shared per role, plus `admin`; three are shown on the login page. Off by default outside the demo. | Load scenarios must authenticate as many different students or the dashboard measurement collapses into one hot cache line; hashing 20,000 passwords on 0.1 CPU would take hours. |
| D8 | Grade lifecycle | Draft → Submitted (lecturer) → Published (administrator, per semester, at a stored instant). Lecturers never publish. | Results day at 09:00 is the product's central moment and a governance step (exam board) sits between marking and release. |
| D9 | Results publication | A stored instant on `results_publications` and `grades.published_at`, evaluated by reads; no scheduler. | A timer on a container that spins down is unreliable; a stored instant is deterministic and testable with a fake clock. |
| D10 | Enrolment history | `enrolments` rows are never deleted; `status` toggles between `Active` and `Withdrawn`; re-enrolment reactivates the row with a conditional update. | Transcripts and audits need the history; the unique (student, module) index still makes duplicates impossible. |
| D11 | Capacity enforcement | One atomic `UPDATE modules SET enrolled_count = enrolled_count + 1 WHERE id = @m AND enrolled_count < capacity`, run last in the transaction; CHECK `enrolled_count >= 0` only. No `enrolled_count <= capacity` CHECK. | A `NOT VALID` CHECK still applies to every updated row, so it would block withdrawals from the already-oversold CS3099 on the live database; the conditional update is the single write path and reconciliation plus the ops page catch drift. |
| D12 | Dashboard | Five set-based queries (student; active enrolments joined to modules; slots for those modules; visible grades joined to modules; announcements) plus cached windows and settings. No per-module cache. Query count is a metric. | Constant query count regardless of module count is what fixes N+1; a timetable cache adds invalidation for no measured gain. |
| D13 | Catalogue | `GET /api/modules` is viewer-agnostic, cached in-process 30 s (HybridCache, no L2); the student's own state comes from `GET /api/me/enrolments`; module detail reads the row uncached. | A viewer-agnostic list is served from cache with zero database work during a rush; fresh counts belong on the detail page. |
| D14 | Pool and shedding | Npgsql `Maximum Pool Size=20`, `Timeout=5`, `Command Timeout=10` in Production (40 locally for load runs); a global concurrency limiter on `/api` (24 permits, 96 queued on Render; 64/256 locally) answering 503 with `Retry-After`. | Neon's free compute grants ~112 connections; 20 leaves headroom, and excess requests must fail fast rather than queue on Postgres. |
| D15 | Health | `/api/health/live` (no DB, Render's health check) and `/api/health/ready` (DbContext check). The old `/health` is removed. | Every route lives under `/api`; readiness with a DB check would make Render restart a container that cannot fix the database anyway. |
| D16 | Errors | RFC 7807 everywhere; `type` is `urn:rushday:<slug>`; `traceId` always present; the slug catalogue is closed (`02-api.md` section 6). | A stable, enumerable identity per error lets the SPA, tests and k6 key on it. |
| D17 | Enum values | PostgreSQL stores enum-like columns as PascalCase `varchar` with CHECK constraints (EF default string conversion); JSON emits camelCase strings (`JsonStringEnumConverter(JsonNamingPolicy.CamelCase)`). `modules.semester` stays `integer` (1 Autumn, 2 Spring) because changing it is a pointless data migration. | Readable SQL, no renumbering hazards, and camelCase JSON as required. |
| D18 | Metrics | `System.Diagnostics.Metrics` (`Meter "RushDay"` plus ASP.NET Core, Kestrel, rate-limiting and Npgsql meters) aggregated in-process by a `MeterListener` into one-minute buckets and served on `/api/admin/ops/metrics`. No exporter, no OpenTelemetry packages. | Nothing to send metrics to on the free tier; the ops page is the consumer and the ring buffer is bounded. |
| D19 | Load-results data | `load/results/runs.json` labels runs; `load/summarize.mjs` generates `src/RushDay.Web/public/data/load-results.json`, which is committed and served as a static file; a public `/story` page and the admin ops page render it. | The story is already public in the README; a buyer should see the evidence before they have an account. |
| D20 | Front-end toolchain | The installed set stays: TypeScript 6.0.3, Vite 8.3.1 (Rolldown), Vitest 5.0.2, ESLint 10.11.0, `@vitejs/plugin-react` 6.1.1, React Router 7.18.4, TanStack Query 5.104.0, Tailwind 4.3.3. No `eslint-plugin-jsx-a11y`. | The lock file already resolves this set with compatible peers; `eslint-plugin-jsx-a11y` declares ESLint `^9` at most, so accessibility is enforced by axe in Playwright instead. |
| D21 | Front-end location | `src/RushDay.Web`, built into `src/RushDay.Api/wwwroot` (gitignored). Playwright lives in `src/RushDay.Web/e2e`. The interim `wwwroot/index.html` is deleted from git. | The scaffold already exists there and the interim page lets anyone read any student's marks. |
| D22 | Password policy | 12 to 128 characters, no composition rules; rejected when it contains the username or "rushday", has fewer than 4 distinct characters, or is on the embedded blocklist. Lockout 5 failures / 15 minutes. | NIST 800-63B: length and a blocklist beat composition rules. |
| D23 | Integration tests locally | The test factory uses Testcontainers unless `RUSHDAY_TEST_CONNECTION` is set, in which case it creates and drops a database on the given native server. | Docker is not available locally; the same suite must run in both places. |
| D24 | Seed results day in tests | Test factories set `Database:SeedResultsDay` to `2026-01-26T09:00:00Z` (past) so seeded autumn results are visible; the demo keeps `2026-09-28T09:00:00Z`. | Tests must not change behaviour when the calendar passes the demo's results day. |
| D25 | Route prefixes | `/api/auth`, `/api/public`, `/api/health`, `/api/me` (student self), `/api/modules`, `/api/announcements`, `/api/lecturer`, `/api/admin`. Legacy `/students/*`, `/modules`, `/health` are removed. | Student routes never carry a student number, so ownership is structural, not a check. |
| D26 | Time zone | Store UTC (`timestamptz`); the SPA formats in the institution time zone from settings (`Europe/London`), naming the zone where ambiguous. | UK universities read 09:00 as local time; storage stays unambiguous. |

## 7. Conventions (apply everywhere)

- JSON: camelCase properties; enum values camelCase strings; instants ISO-8601 UTC with `Z`; times of day `"HH:mm"`;
  ids are UUID strings; paged lists are `{ items, page, pageSize, total }`.
- SQL: snake_case identifiers via `UseSnakeCaseNamingConvention()`; PascalCase string values for enum-like columns
  with CHECK constraints; `uuid` primary keys from `Guid.CreateVersion7()`; `timestamp with time zone` for instants.
- Routes: everything under `/api`; route constraints `{studentNumber:regex(^S\d{{6}}$)}`, `{code:regex(^[A-Z]{{2}}\d{{4}}$)}`,
  `{staffNumber:regex(^L\d{{5}}$)}`, `{id:guid}`.
- Errors: `application/problem+json`, `type` = `urn:rushday:<slug>`, `traceId` extension always set.
- Warnings are errors in every project; nullable on; `TimeProvider` injected, never `DateTimeOffset.UtcNow` in
  application code.
- Demo credentials: `S000001` / `Student-Demo-2026!`, `L00001` / `Lecturer-Demo-2026!`, `admin` / `Admin-Demo-2026!`.

## 8. Acceptance checklist: "production-ready" for a university IT buyer

Each item names how it is demonstrated. All are Must unless marked. The release stage (`06-implementation-plan.md`
stage S13) ticks this list in the pull request description.

**Story**
- [ ] The story sentence pair appears verbatim in the five locations of section 1 (grep in CI: `scripts/check-story.ps1`).

**Identity and access**
- [ ] No self-registration: `MapIdentityApi` is not referenced anywhere; integration test `POST /api/auth/register` → 404 ProblemDetails.
- [ ] Roles enforced server-side: `/api/me/*` requires `StudentOnly` and derives identity from claims; `/api/lecturer/*` requires `LecturerOnly` and `TeachesModule` where a module code appears; `/api/admin/*` requires `AdminOnly`. Integration tests cover each cross-role attempt → 403.
- [ ] A student can never read another student's data: no student-role route takes a student number. Test: S000001's session on `/api/admin/students/S000002` → 403.
- [ ] Lockout after 5 failures for 15 minutes; login errors are generic and constant-time; lockouts are audited (`auth.locked_out`).
- [ ] Sessions: `rushday.auth` is HttpOnly, Secure (outside Development), SameSite=Strict; 8 h sliding, 12 h absolute; security stamp validated every 5 minutes so disable, lock and password reset take effect; logout invalidates.
- [ ] Antiforgery token required on every mutating `/api` request; test: POST without header → 400 `urn:rushday:antiforgery`.
- [ ] Forced password change for provisioned and reset accounts (`must_change_password`); every other route answers 403 `password-change-required` until done.
- [ ] Should: Entra ID / SAML SSO documented as the Identity external-login extension point in `docs/admin-guide.md`.

**Data protection (marks are personal data under UK GDPR)**
- [ ] Unpublished marks unreachable by students: integration tests prove Draft, Submitted and future-Published marks are absent from `/api/me/dashboard` and `/api/me/results`, and appear once a fake clock passes `published_at`.
- [ ] Every enrolment, mark, publication and account change writes an `audit_events` row in the same transaction with actor, time, before/after and reason; the table is append-only from the application; export is CSV.
- [ ] Demo mode is a single switch (`Demo:Enabled`), off by default; with it off the login page shows no credentials and `GET /api/public/status` returns `demo: null`.
- [ ] TLS only (Render terminates; app sets HSTS behind `ForwardedHeaders`); security headers per `03-security.md` section 4 on every response; no third-party scripts, fonts or connections on any page (CSP `connect-src 'self'`).
- [ ] Logs carry no names, marks, passwords or request bodies (student numbers and user ids allowed).

**Correctness under load, with evidence**
- [ ] `load/k6/enrolment-rush.js` (500 authenticated students, 30 places): accepted = 30, `OVERSOLD=0`, no TCP refusals, every rejection a fast 409 or 503. Before/after table in `docs/load-results/2026-10-xx-v1-hardened.md` next to the v0 numbers (154 accepted, 124 oversold, 69% failed).
- [ ] `load/k6/results-day.js` authenticated at 800 rps: `http_req_failed` < 1%, p95 < 500 ms, `rushday.dashboard.queries` = 5 per request (was ~15).
- [ ] `load/k6/dashboard-knee.js` beyond saturation: excess requests receive 503 with `Retry-After` within 1 s instead of multi-second failures; zero Postgres 53300 errors in the API log.
- [ ] `load/k6/login-storm.js` documents the PBKDF2 cost and the login limiter's behaviour.
- [ ] Integration test: 200 parallel authenticated enrolments on a 30-place module → exactly 30 × 201, `enrolled_count = COUNT(*) = 30`.
- [ ] ADRs 0007 to 0012 written, each linking its before and after runs.

**Operability**
- [ ] `/api/health/live` is Render's health check; `/api/health/ready` reflects database reachability; startup applies the migration and backfills idempotently and logs each step; a crash mid-backfill is recoverable by restart (integration test runs the backfills twice and asserts identical counts).
- [ ] Ops page shows live request rate, p95/p99, 5xx count, rate-limit and load-shed rejections, pool busy/idle/max and wait timeouts, memory; alerting is out of scope on the free tier and the page says so.
- [ ] Memory during the 800 rps run stays below 350 MB working set (recorded in the load-results document).
- [ ] Structured JSON logs in Production with `traceId`, `userId`, `role`, route; `traceId` appears in every ProblemDetails so a user report maps to a log line.
- [ ] Backup and restore: Neon point-in-time restore documented in `docs/deployment.md` with one rehearsed restore of the demo database recorded.
- [ ] Configuration is environment-only; no secrets in git or logs; `dotnet list package --vulnerable --include-transitive` and `npm audit --audit-level=high` clean in CI.

**Quality and accessibility**
- [ ] CI gates deploy (`autoDeployTrigger: checksPass`): `web` (lint, typecheck, unit tests, build, bundle budget), `build-and-test` (unit, Testcontainers integration, Docker image), `e2e` (Playwright journeys and axe) are all required checks.
- [ ] Playwright journeys green: sign in as each role; student enrols and withdraws; lecturer enters and submits marks; admin publishes and the student sees results; admin provisions an account and its first login forces a password change; admin disables an account and that session dies; guards and deep links; mobile viewport without horizontal scroll.
- [ ] WCAG 2.2 AA: axe (`wcag2a, wcag2aa, wcag22aa`) on every page in both themes with zero `serious` or `critical` violations; keyboard-only path through login, enrol and marks entry; public `/accessibility` statement page.
- [ ] Documentation current: `README.md` (story, architecture, run, test, load, demo accounts), `docs/admin-guide.md`, `docs/deployment.md`, ADRs, load results, this spec.
