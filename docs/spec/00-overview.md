# RushDay v1 specification: 00. Overview

Status: accepted. Written 2026-09-27; revised 2026-09-29 after a three-critic review (university IT security buyer,
implementing lead engineer, first-time student/lecturer/registry user) against commit `a73b447` on branch `v1`. The
`src/RushDay.Web` scaffold is committed; `src/RushDay.Api/wwwroot` is gitignored and untracked. Stage S1 is
implemented (`a73b447`); what it must change to match this revision is listed in `06-implementation-plan.md`
section 5 ("S1 conformance delta").
This folder (`docs/spec/00` to `06`) is the single source of truth for v1. Where an ADR, README or design note
disagrees with these files, these files win and the other document is updated.

The seven files:

| File | What it fixes |
|---|---|
| `00-overview.md` | The story, personas, academic processes, scope (Must/Should/Could/Won't), the decision register, and the acceptance checklist for "production-ready". |
| `01-domain-and-data.md` | Entities, tables, columns, constraints, indexes, the one migration, the idempotent startup backfills for the populated Neon database, demo accounts. |
| `02-api.md` | Every route under `/api` with request and response JSON, status codes, role requirements; session, MFA, antiforgery, rate-limit policies, ProblemDetails. |
| `03-security.md` | Threat model, controls, exact headers and CSP, configuration and secrets per environment, logging and audit rules, what is out of scope. |
| `04-performance-and-ops.md` | The enrolment concurrency fix, query shapes, caching, pool and timeouts, load shedding, metrics, health, the k6 changes and how before/after evidence is captured. |
| `05-frontend.md` | Stack, structure, routing, auth flow, data layer, design system, page-by-page spec, accessibility, tests, build integration. |
| `06-implementation-plan.md` | Ordered stages with file ownership, definition of done and local verification (no Docker, no `dotnet run`). |

## 1. The story

This sentence pair is the owner's story and appears **verbatim** wherever the project describes itself:

> I built RushDay because my own university's portal fell over every results day and enrolment window. I wanted to understand why that happens and to build a portal that doesn't crash under the same load.

Required locations (each is a checklist item in section 8):

1. `README.md`, first paragraph after the title.
2. `GET /api` index, the `story` field.
3. The login page (`/login`, rendered as a `<blockquote>` with `<cite>Ahmed Ayyan, creator of RushDay</cite>`) and the
   opening of the public `/story` page (string literals in `LoginPage.tsx` and `StoryPage.tsx`; both are grepped).
   In demo mode the quote is the strapline under the wordmark; when demo mode is off the strapline is "{institution}
   student portal" and the quote sits below the form under the heading "About RushDay", so a customer's students see
   their own institution first and know whose words the quote is (`05-frontend.md` section 10).
4. `src/RushDay.Web/index.html` `<meta name="description">`.
5. `docs/adr/0007-identity-and-cookie-sessions.md`, Context section (the first v1 ADR).

The measured narrative stays central: v0 was deliberately naive (ADR 5), the baseline broke in three ways
(`docs/load-results/2026-09-27-v0-baseline.md`: 154 places handed out for 30, Postgres error 53300 pool
exhaustion, 260 TCP refusals; dashboard knee between 800 and 1,000 rps), and v1 fixes each with an ADR and a
before/after run. v1 also turns RushDay into something a university could buy: three roles, real academic
processes, authentication and authorization (with a second factor for staff), security by default, an ops view,
and evidence.

## 2. What RushDay v1 is

A single container (ASP.NET Core 10 minimal API serving a React 19 SPA from `wwwroot`) backed by one PostgreSQL
database (Neon free tier in the cloud, PostgreSQL 18 locally). Students see marks the moment they are published,
enrol on modules while places last, withdraw before a deadline and read announcements. Lecturers see rosters,
enter and submit marks, and post module announcements. Administrators run enrolment windows, publish results at a
chosen instant, correct and unpublish when the exam board says so, fix enrolments with a reason, provision
accounts, keep student records current, and watch the system. Everything a person does that changes marks,
enrolments or accounts is audited, and the audit table cannot be rewritten even by the application's own database
role.

Hard constraints (from the brief, restated so no stage relaxes them):

- .NET 10 backend, EF Core 10 + Npgsql + PostgreSQL. React 19 + TypeScript + Vite + Tailwind CSS 4 front end built
  into `src/RushDay.Api/wwwroot`. One container, one URL. All API routes under `/api`.
- Free tier survives: Render web service (0.1 CPU, 512 MB, one instance) + Neon PostgreSQL. No Redis, no paid
  add-ons, in-process caching only, memory-conscious.
- The Neon database is already seeded with the 20,000-student dataset. Every schema change is an EF migration that
  works on a populated database; every data backfill is an idempotent startup step.
- No self-registration. The university provisions accounts. Demo accounts (one per role) are shown on the login page
  only when demo mode is on, and every demo account is disabled the moment demo mode is turned off.
- The k6 scenarios keep working, so they authenticate.
- No Docker locally: integration tests run against Testcontainers in CI and against the native PostgreSQL 18 locally
  (`RUSHDAY_TEST_CONNECTION`), see `06-implementation-plan.md`.
- No `dotnet run` locally: Windows Smart App Control blocks it on this machine. Every local run of the API goes through
  `scripts/run-api.ps1` (single-file publish, then start the exe), see `06-implementation-plan.md` section 1.

## 3. Personas

| Persona | In the data | Jobs to be done | What "the portal fell over" costs them |
|---|---|---|---|
| **Student** | One of 20,000 `students` rows (`S000001`..`S020000`), login = student number. | See my marks the moment they are published. Enrol on the modules I want before places go. Know my timetable. Withdraw before the deadline. Read announcements. See what I completed last year. | Refreshing a dead page at 09:00; losing a place on CS3099 to a request that happened to land; not knowing whether a click "worked". |
| **Lecturer** | One of 40 `lecturers` rows (`L00001`..`L00040`), assigned to modules through `module_lecturers`, login = staff number. | See who is on my modules this year. Enter marks as drafts, record absences and deferrals, correct them, and (as leader) submit the module when complete. Post module announcements. Never show a student an unpublished mark. | Marks visible before the exam board signed off; spreadsheets emailed around; roster mismatches. |
| **Administrator** | The administrator account (`admin` on the demo; `Bootstrap:AdminUsername` for a customer), no student or lecturer record, protected by a password and a TOTP second factor. | Open and close enrolment windows. Set the results-day instant and publish what lecturers submitted; cancel, unpublish or correct when the exam board says so. Fix enrolments by hand with a written reason. Keep student and lecturer records current. Provision, lock, reset and disable accounts. See who did what, including who exported what. See that the system is healthy and how it behaved under load, in plain words. | Being the person on the phone at 09:05 while the site is down; oversold modules nobody can untangle; no record of who changed a mark. |
| **University IT buyer** (not a login) | Evaluates the repository, the live demo and the checklist in section 8. | Prove it is secure, accessible, observable, recoverable, deployable for their own institution without demo data, and that the load claims reproduce. | Buying another portal that dies on results day. |

## 4. Academic processes the product models

All time comparisons use `TimeProvider.GetUtcNow()` at request time. **There is no background scheduler.** Anything
that "happens at 09:00" is a stored instant that reads evaluate against; on a free-tier container that spins down
when idle a timer is a lie, a stored instant is deterministic, testable with a fake clock and survives restarts.

### 4.1 Academic year and enrolment windows

- One `academic_settings` row names the current `academic_year` (`"2026/27"` on the demo), the institution name and
  the display time zone (`Europe/London`).
- One `enrolment_windows` row per (`academic_year`, `semester`): `opens_at`, `closes_at`, `withdrawal_deadline_at`.
- **Every enrolment carries the academic year it belongs to** (`enrolments.academic_year`, D28). A student's
  "current modules", timetable and credit budget are the active enrolments whose `academic_year` equals the settings
  year; earlier years are their "completed modules". Grades inherit the year through the enrolment they belong to,
  so results are grouped by (year, semester) and a publication for (year, semester) touches only that year's marks.
- A student may self-enrol on module M iff a window exists for (current year, M.semester) and
  `opens_at <= now < closes_at`. Self-withdrawal is allowed iff `now < withdrawal_deadline_at` and no submitted or
  published mark exists. A module the student already holds a submitted or published mark for cannot be taken
  again in v1 (409 `results-exist`; retakes are Could). Nobody joins a module whose marks for the year have already
  been submitted, scheduled or published (409 `module-locked`, section 4.3), not even by an administrator's override.
- Outside a window the catalogue still shows every active module with `enrolmentState` = `notYetOpen | open | closed`
  and the instants, so students can plan.
- Demo data: Autumn 2026/27 window open 2026-09-14 09:00 to 2026-10-02 17:00 UTC (withdrawal deadline 2026-10-30
  17:00) and Spring 2026/27 open 2026-09-14 09:00 to 2027-01-29 17:00 UTC (withdrawal deadline 2027-02-26 17:00).
  The 80,000 seeded enrolments belong to **2025/26** (their marks are published), so every seeded student starts
  2026/27 with 0 of 60 credits in each semester and can enrol on autumn and spring modules alike. The demo backfill
  additionally enrols the first 100 year-1 students (`S000001`, `S000004`, …, `S000298`) on `CS3001` for Autumn
  2026/27 (no marks yet) so the demo lecturer has a cohort to mark. The credit rule is exercised by `EnrolmentTests`
  (five autumn modules), not by the seed.
- One `academic_settings.current_semester` (Autumn on the demo) says which semester is being taught now; it decides
  whose classes appear on this week's timetable. Administrators change it on `/admin/settings`.

### 4.2 Module capacity

- Capacity is a hard invariant: `modules.enrolled_count <= modules.capacity` for every module at every instant, where
  `enrolled_count` is the number of `enrolments` rows with `status = 'Active'` **in the current academic year** (last
  year's completed enrolments never take a place this year). It is enforced by a single atomic
  conditional `UPDATE ... WHERE enrolled_count < capacity` inside the enrolment transaction (`04-performance-and-ops.md`
  section 2). No read-then-write path exists.
- v1 is first come, first served. When full, the student gets a fast 409 `module-full`; the catalogue shows "Full"
  with the live count; withdrawing frees a place immediately.
- Waiting lists are Should (section 5) because promotion is a second race with a notification requirement that v1 has
  no channel for.
- The live database inherited an oversold CS3099 from v0 (154 active rows for 30 places). In demo mode every start
  withdraws the self-service CS3099 enrolments that carry no mark (the v0 rows first of all), so the demo's hot module
  has its 30 places again after each deploy or idle restart, and the demo administrator can do the same on demand
  from the ops page. A customer resolves any drift with the admin "trim to capacity" action, never with SQL.

### 4.3 Results: draft, submitted, scheduled, published

Each (`student`, `module`) grade row has `status` and an `outcome` (`Mark`, `Absent`, `Deferred`; only `Mark` carries
a number):

```
Draft ──leader submits module──▶ Submitted ──admin publishes (year, semester) at publish_at──▶ Published
  ▲                                   │  ▲                                                        │
  │                                   │  └──── admin cancels a scheduled publication ─────────────┤ (publish_at > now)
  └──── admin returns to draft ───────┘  ◀──── admin unpublishes a live publication ──────────────┘ (publish_at <= now)
       (also allowed while the module's publication is still scheduled)
```

- **Draft**: the module's lecturers create and edit marks. Invisible to students. Visible to the module's lecturers
  and to administrators.
- **Submitted**: the module leader declared the module complete; every active enrolment has a mark or a non-mark
  outcome. Locked for lecturers. An administrator may return it to Draft with a reason.
- **Published**: an administrator publishes a (year, semester): every Submitted grade on that year's enrolments in
  that semester gets `status = 'Published'`, `published_at = publish_at`, `publication_id = <the results_publications
  row>`. `publish_at` may be the future (the 09:00 results-day moment, at most 90 days ahead) or the past (immediate).
  While `publish_at > now` the publication is **scheduled**: it can be rescheduled, cancelled (grades back to
  Submitted), or a module can be returned to draft. Once live it can be **unpublished** with a reason (grades back
  to Submitted), and any single mark can be **corrected** with a reason at any point after submission.
- **The visibility rule that must be impossible to get wrong:** a student sees a grade iff
  `status = 'Published' AND published_at <= now` **and the student's enrolment on that module is `Active`** (a
  student who withdrew, or was withdrawn, never sees a mark for the module). This is one query filter
  (`GradeQueries.VisibleToStudents(db, now)`) that every student-facing read goes through. Student response contracts
  carry no field that could hold a draft mark. Only grades of active enrolments are ever submitted or published;
  withdrawn students' drafts stay Draft. **No active student's mark is ever stranded** (review S6 E1): once a module's
  marks for the year have left draft nobody can join it (409 `module-locked`) until an administrator returns it to
  draft, when the returning student's draft is there again for the lecturers; and a module is publishable only when
  every active enrolment's grade is Submitted (a Draft on an active enrolment counts as missing), so a publish never
  reports a module published while leaving a student's mark behind. Before `published_at` the student sees "Autumn 2025/26 results publish on
  28 Sep 2026 at 10:00 (Europe/London)", formatted by the browser. A corrected mark is labelled "Amended {date}".
- The 80,000 seeded grades become `Published` with their existing `published_at` (2026-09-28 09:00 UTC) and a
  seed `results_publications` row for (2025/26, autumn). On the live demo that instant is in the past from release
  day onward, so the login page and dashboard show the **published** line ("Autumn 2025/26 results published 28 Sep
  2026 at 10:00"); the results-day countdown appears whenever an administrator schedules a future publication, and
  `docs/admin-guide.md` carries the recipe for re-running results day on the demo (enter marks for CS3001 as L00001,
  submit, publish Autumn 2026/27 at a future instant).

### 4.4 Announcements

`scope = 'University'` (administrators) or `'Module'` (the module's lecturers and administrators). Plain text, rendered
with line breaks only. `published_at` may be in the future; optional `expires_at`; `pinned` sorts first.
Students see university announcements plus those of modules they are actively enrolled on this year; lecturers see
university plus their modules; administrators see all. Publishing results can post a pinned university announcement
in the same transaction; it follows the publication (a reschedule moves it; a cancel, an unpublish or a return to
draft that empties the publication deletes it), so "results are available" never appears before the results or
outlives them.

### 4.5 Timetable

Data unchanged (`timetable_slots`). Students see slots of this year's active enrolments on modules of the **current
semester** (`academic_settings.current_semester`), each block naming the module, the room and whether it is a
lecture or a lab; lecturers see slots of their modules. Timetable clash on enrolment is a Should **warning** in the
UI, never a block: the seeded timetables are random and a hard block would change the enrolment-rush numbers for the
wrong reason.

## 5. Scope: MoSCoW per role (Must = buyable v1)

### Student

| Priority | Feature |
|---|---|
| Must | Sign in with student number; sign out (ends every session of the account); change password; forced password change on first login of a provisioned account. |
| Must | Dashboard: name, programme, year; this year's active modules with credits; completed modules from earlier years with their published mark; published results with credit-weighted average and classification; next or latest results publication with a live countdown that refreshes the page's data at the instant; enrolment window state and credit budget; announcements; the week's timetable with today first. |
| Must | Module catalogue with live `placesRemaining`, semester, credits, lecturers, window state, my enrolment state (enrolled, withdrawn, completed); enrol; clear reason on rejection (full, already enrolled, credit limit, window closed, already completed). |
| Must | Withdraw from a module before the withdrawal deadline (blocked when a submitted or published mark exists; the UI says why before the click). |
| Must | Full weekly timetable; results page by (year, semester) with scheduled instants for unpublished semesters; absences and deferrals shown as such. |
| Must | Personal data export (`GET /api/me/export.json`). |
| Should | Waiting list; timetable-clash warning; iCal export of the timetable. |
| Could | Module prerequisites; retaking a completed module. |
| Won't (v1) | Self-registration; self-service password reset; fee payment; coursework submission; messaging. |

### Lecturer

| Priority | Feature |
|---|---|
| Must | Sign in with staff number; my modules with this year's enrolled count, capacity and marks status (no students / draft / submitted / scheduled / published, entered / missing). |
| Must | Roster per module (student number, name, programme, year, enrolled at, status), searched by number prefix or any part of the name. |
| Must | Marks entry per module: paged editable grid of draft marks (100 per page, search), outcome per row (mark / absent / deferred), save dirty rows in chunks (all-or-nothing per chunk with optimistic `version`), unsaved marks survive a session expiry, who changed what and when; the **module leader** submits the module when complete, otherwise a 422 listing missing students; teachers save drafts only. |
| Must | Read-only view of submitted, scheduled and published marks for my modules with the instant. |
| Must | Post, edit and delete announcements on my modules (and only on my modules). |
| Should | TOTP second factor, enrolled voluntarily from the account page; CSV export of the roster; CSV import of marks; my weekly teaching timetable. |
| Could | Free-text mitigating-circumstances notes per student. |

### Administrator

| Priority | Feature |
|---|---|
| Must | TOTP second factor, mandatory: an administrator without it can only reach the MFA setup routes. |
| Must | Enrolment windows: create, edit, delete per (academic year, semester). |
| Must | Results: submission progress per module for a (year, semester), modules without students hidden by default; publish at an instant (now or future, at most 90 days ahead), optionally posting a pinned announcement; publication history; reschedule or cancel a scheduled publication; unpublish a live publication with a reason; return a module to draft with a reason (draft or scheduled); correct a single mark with a reason (submitted or published). |
| Must | Students: create, edit (name, programme, year, email), mark as left (withdraws this year's enrolments that hold no submitted or published mark, disables the account); search by number prefix or name fragment; view a student as the student sees it plus statuses (the view is audited); override-enrol and override-withdraw with a mandatory reason (ignores windows and the credit limit; capacity applies unless `forceCapacity` raises capacity by one **only when the module is full**, audited); personal data export on the student's behalf. |
| Must | Modules: create and edit title, description, credits, capacity (never lowered below `enrolled_count`; an unchanged capacity is always accepted), semester (only while the module has never had an enrolment or a mark, in any year), active flag; assign lecturers (exactly one leader; a lecturer who has left cannot be assigned); read-only roster and marks sheet for any module; trim an over-capacity module back to capacity with a reason (counted from the real enrolments). |
| Must | Lecturers: create; edit (name, title, department, email); mark as left (their assignments stay on record without authority, and they get no account again); list. |
| Must | Accounts: provision a user for an existing student or lecturer or a new administrator with a temporary password shown once; lock, unlock, disable, enable, reset password (all set "must change password"), reset the second factor; demo accounts are read-only. |
| Must | University-wide announcements. |
| Must | Audit log filtered by actor, student, module, action, date range; CSV export (itself audited, truncation signalled). |
| Must | Ops page in plain language: a one-line health summary, live request rate, p95 and p99, error rate, load-shed and rate-limit rejections, DB pool usage and wait timeouts, memory; the load-results story with before/after charts; backfill status; data-quality warnings (modules over capacity, `enrolled_count` drift) with reconcile and trim actions. The page keeps working while the API is shedding load. |
| Should | Bulk CSV import of students, lecturers and assignments; recovery codes for the second factor; audit chain hash for tamper evidence; Microsoft Entra ID single sign-on through Identity external login (the first post-v1 request any UK university makes). |
| Could | Timetable slot editing; audit retention purge; academic-year rollover wizard; retakes. |

## 6. Decision register

Every disagreement between the three input designs is resolved here with one sentence of rationale. Later files
elaborate; they never reopen.

| # | Topic | Decision | Rationale |
|---|---|---|---|
| D1 | Identity store | ASP.NET Core Identity with the EF Core store, `ApplicationUser : IdentityUser<Guid>`, tables renamed to snake_case (`users`, `roles`, ...). No `MapIdentityApi`, no registration route. | Proven hashing, lockout, security stamps and TOTP for free; registration is the only part we do not want. |
| D2 | Role names | `Student`, `Lecturer`, `Admin`; exactly one role per user. | Short names match the URL areas `/student`, `/lecturer`, `/admin`; one role per user removes precedence rules. |
| D3 | Lecturer identity | A domain `lecturers` table mirrors `students`; `users.lecturer_id` links them; `module_lecturers` references `lecturers`, not `users`. | Domain data must not depend on the login store, and a lecturer without a login is a valid record. |
| D4 | Session | Cookie authentication (`__Host-rushday.auth` outside Development, `rushday.auth` in Development; HttpOnly, Secure, SameSite=Strict, Path=/). Lifetimes enforced per role in the cookie validation event: students and lecturers 8 h sliding / 12 h absolute, administrators 60 min sliding / 8 h absolute; security stamp validated every 5 minutes; logout rotates the security stamp so every session of the account ends. JWT rejected. | Same-origin SPA: an HttpOnly cookie is unreadable by script and revocable; a JWT needs script-readable storage and a denylist; the `__Host-` prefix stops sibling hosts planting cookies on a customer domain. |
| D5 | Antiforgery | Header `X-CSRF-TOKEN` on every non-GET `/api` request including login; the token is returned in the JSON of `GET /api/auth/csrf`, `POST /api/auth/login`, `POST /api/auth/mfa/verify` and `GET /api/auth/me` (never as a readable cookie). | The SPA never parses cookies, the token rotates with the principal, and k6 follows the same two calls. |
| D6 | Login rate limiting | One named ASP.NET policy `login` (per client IP, 600/min) on the endpoint, plus `LoginThrottle` in the handler: per-username window of **failed** outcomes (10/min), a per-address window of **failed** outcomes (20 per 10 min; IPv6 by /64), both reserved before the password check and refunded on success, and a concurrency guard around PBKDF2 (8 concurrent, 16 queued, below the global limiter's 24 permits). Identity lockout (5 failures / 15 min) is only triggered when an account's failures arrive from at least 3 distinct addresses; single-address attacks are stopped by the per-IP failure window without locking the victim. | A campus NAT puts a hall of residence behind one address, PBKDF2 CPU is the scarce resource on 0.1 vCPU, and enumerable usernames make naive lockout a denial-of-service tool; the residual risk (a distributed spray) is recorded in `03-security.md`. |
| D7 | Demo accounts | In demo mode all 20,000 students and all 40 lecturers get logins with one precomputed password hash shared per role, plus `admin`; three are shown on the login page. Every such account carries `is_demo`, cannot be locked, mutated or given a second factor, is healed on every start, and is **disabled on the first start with demo mode off** (D29). | Load scenarios must authenticate as many different students or the dashboard measurement collapses into one hot cache line; hashing 20,000 passwords on 0.1 CPU would take hours; a customer must never be left with published passwords. |
| D8 | Grade lifecycle | Draft → Submitted (module leader) → Published (administrator, per year and semester, at a stored instant); administrators may cancel a scheduled publication, unpublish a live one, return a module to draft, and correct single marks, all with reasons. Lecturers never publish. | Results day at 09:00 is the product's central moment and a governance step (exam board) sits between marking and release; the board also finds errors afterwards, and SQL is not an acceptable correction path. |
| D9 | Results publication | A stored instant on `results_publications` and `grades.published_at`, evaluated by reads; no scheduler. | A timer on a container that spins down is unreliable; a stored instant is deterministic and testable with a fake clock. |
| D10 | Enrolment history | `enrolments` rows are never deleted; `status` toggles between `Active` and `Withdrawn`; re-enrolment reactivates the row with a conditional update and stamps the current academic year. | Transcripts and audits need the history; the unique (student, module) index still makes duplicates impossible. |
| D11 | Capacity enforcement | One atomic `UPDATE modules SET enrolled_count = enrolled_count + 1 WHERE id = @m AND enrolled_count < capacity`, run last in the transaction; CHECK `enrolled_count >= 0` only. No `enrolled_count <= capacity` CHECK. | A `NOT VALID` CHECK still applies to every updated row, so it would block withdrawals from the already-oversold CS3099 on the live database; the conditional update is the single write path and reconciliation plus the ops page catch drift. |
| D12 | Dashboard | Five set-based queries (student; active enrolments of every year joined to modules, partitioned in memory into current and completed; slots for this year's modules of the current semester; visible grades joined to modules and enrolments; announcements) plus cached windows, settings and publications. No per-module cache. Query count is measured by an EF command interceptor, not asserted by hand. | Constant query count regardless of module count is what fixes N+1; a timetable cache adds invalidation for no measured gain. |
| D13 | Catalogue | `GET /api/modules` is viewer-agnostic, cached in-process 30 s (HybridCache, no L2); the student's own state comes from `GET /api/me/enrolments`; module detail reads the row uncached. | A viewer-agnostic list is served from cache with zero database work during a rush; fresh counts belong on the detail page. |
| D14 | Pool and shedding | Npgsql `Maximum Pool Size=20`, `Minimum Pool Size=0`, `Timeout=5`, `Command Timeout=10` in Production (40 locally for load runs); a global concurrency limiter on `/api` (24 permits, 96 queued on Render; 64 permits, 1,024 queued locally so the 500-VU rush is never shed by the limiter) answering 503 with `Retry-After`. `/api/health/live` and `/api/admin/ops/metrics` sit outside it. | Neon's free compute grants ~112 connections; 20 leaves headroom, excess requests must fail fast rather than queue on Postgres, and the ops page must answer while everything else is shedding. |
| D15 | Health | `/api/health/live` (no DB, Render's health check, unlimited) and `/api/health/ready` (DbContext check, per-IP limited to 30/min). The old `/health` is removed. | Every route lives under `/api`; readiness with a DB check would make Render restart a container that cannot fix the database anyway; an unlimited anonymous readiness probe would be a pool-exhaustion tool. |
| D16 | Errors | RFC 7807 everywhere; `type` is `urn:rushday:<slug>`; `traceId` always present; the slug catalogue is closed (`02-api.md` section 6). | A stable, enumerable identity per error lets the SPA, tests and k6 key on it. |
| D17 | Enum values | PostgreSQL stores enum-like columns as PascalCase `varchar` with CHECK constraints (EF default string conversion); JSON emits camelCase strings (`JsonStringEnumConverter(JsonNamingPolicy.CamelCase)`). `modules.semester` stays `integer` (1 Autumn, 2 Spring) because changing it is a pointless data migration. | Readable SQL, no renumbering hazards, and camelCase JSON as required. |
| D18 | Metrics | `System.Diagnostics.Metrics` (`Meter "RushDay"` plus ASP.NET Core, Kestrel, rate-limiting and Npgsql meters) aggregated in-process by a `MeterListener` into one-minute buckets and served on `/api/admin/ops/metrics`. No exporter, no OpenTelemetry packages. | Nothing to send metrics to on the free tier; the ops page is the consumer and the ring buffer is bounded. |
| D19 | Load-results data | `load/results/runs.json` labels runs; `load/summarize.mjs` generates `src/RushDay.Web/public/data/load-results.json`, which is committed and served as a static file; a public `/story` page and the admin ops page render it. | The story is already public in the README; a buyer should see the evidence before they have an account. |
| D20 | Front-end toolchain | The installed set stays: TypeScript 6.0.3, Vite 8.3.1 (Rolldown), Vitest 5.0.2, ESLint 10.11.0, `@vitejs/plugin-react` 6.1.1, React Router 7.18.4, TanStack Query 5.104.0, Tailwind 4.3.3. No `eslint-plugin-jsx-a11y`. | The lock file already resolves this set with compatible peers; `eslint-plugin-jsx-a11y` declares ESLint `^9` at most, so accessibility is enforced by axe in Playwright instead. |
| D21 | Front-end location | `src/RushDay.Web` (committed), built into `src/RushDay.Api/wwwroot` (already gitignored and untracked). Playwright lives in `src/RushDay.Web/e2e`. The interim `wwwroot/index.html` exists only in working trees; S2 deletes it from the working tree, nothing is removed from git. | The scaffold already exists there and the interim page lets anyone read any student's marks. |
| D22 | Password policy | 12 to 128 characters, no composition rules; rejected when it contains the username or "rushday", has fewer than 4 distinct characters, equals the current password, or is on the embedded blocklist (top 10,000 breached passwords of 12+ characters). PBKDF2-HMAC-SHA512 at 210,000 iterations. Lockout 5 failures / 15 minutes (see D6 for when failures count). | NIST 800-63B: length and a blocklist beat composition rules; OWASP's current PBKDF2 figure. |
| D23 | Integration tests locally | The test factory uses Testcontainers unless `RUSHDAY_TEST_CONNECTION` is set, in which case it creates and drops a database on the given native server (connecting to the `postgres` maintenance database as the same role, which has `CREATEDB`). | Docker is not available locally; the same suite must run in both places. |
| D24 | Seed results day in tests | Test factories set `Database:SeedResultsDay` to `2026-01-26T09:00:00Z` (past) so seeded autumn results are visible, and start the fake clock at `2026-09-27T12:00:00Z`; the demo keeps `2026-09-28T09:00:00Z`. | Tests must not change behaviour when the calendar passes the demo's results day. |
| D25 | Route prefixes | `/api/auth`, `/api/public`, `/api/health`, `/api/me` (student self), `/api/modules`, `/api/announcements`, `/api/lecturer`, `/api/admin`. Legacy `/students/*`, `/modules`, `/health` are removed. | Student routes never carry a student number, so ownership is structural, not a check. |
| D26 | Time zone | Store UTC (`timestamptz`); the SPA formats in the institution time zone from settings (`Europe/London`), naming the zone where ambiguous. The server stores the zone id as text and validates its shape; only the browser (`Intl`) formats with it, so no server-generated text (demo hints, seeded or automatic announcements) contains a formatted date or time. | UK universities read 09:00 as local time; storage stays unambiguous; the server runs with invariant globalization on a chiseled image without time-zone data. |
| D27 | Second factor | TOTP (RFC 6238 over Identity's authenticator key, ±2 steps, each code accepted once; `02-api.md` section 2.4): Must for `Admin` (an administrator without it can reach only the MFA setup routes), Should for `Lecturer` (voluntary), never for `Student` in v1. The demo administrator is exempt only while demo mode is on. Recovery codes are Should; SSO stays Should. | Cyber Essentials requires MFA on administrative accounts; TOTP needs no email or SMS channel, so the free tier is no excuse. |
| D28 | Academic year on enrolments | `enrolments.academic_year` (seeded rows `2025/26`), set from the settings row at enrolment time; grades take their year from their enrolment; every staff read of enrolments and grades is scoped to a year; publication targets a (year, semester); `modules.enrolled_count` counts active enrolments of the **current** year only, so changing the settings year recomputes it. | Without it last year's modules are this year's "current modules", the credit budget is permanently full, last year's cohort occupies this year's places, and results of successive years merge. |
| D29 | Demo lifecycle | Demo mode is `Demo:Enabled`, off by default; in Production it also requires `Demo:PublicDemoAcknowledged=true` (else startup aborts) and logs a warning on every start; the seeder runs only in demo mode or under `--migrate-and-seed`; a start with demo off disables every `is_demo` account and rotates its security stamp; `render.yaml` never sets demo or seeding values. | A university applying the Blueprint must not get 20,041 accounts with published passwords, and switching demo off after an evaluation must actually switch it off. |
| D30 | Audit immutability | A PostgreSQL trigger (`trg_audit_events_immutable`) rejects every UPDATE and DELETE on `audit_events`; reads of bulk personal data (audit export, student view, self export) are themselves audited. Chain hash is Should. | "The application has no such code path" is not a control against SQL injection, a raw-SQL mistake or a compromised container; a subject-access request must be answerable. |
| D31 | Data Protection key ring | Keys persist in `data_protection_keys` encrypted with AES-256-GCM under `DataProtection:KeyEncryptionKey` (required in Production; Development keeps plain keys). Losing the variable invalidates every session and nothing else. | A stolen database dump must not let an attacker mint a valid cookie for any user. |
| D32 | IP pseudonymisation | `ip_hash = hex(HMAC-SHA256(dailyKey, clientIp))[..32]` where `dailyKey = HMAC-SHA256(DataProtection:KeyEncryptionKey, "rushday.ip-hash:" + yyyy-MM-dd)`, never stored; Development uses a per-process random key. Used for `audit_events.ip_hash`, lockout audit details and the failed-login log line. | A date-derived salt over the IPv4 space is brute-forced in seconds; a keyed daily hash is not reversible without the key ring. |
| D33 | Registry access to modules | Administrators read any module's roster and marks sheet through `GET /api/admin/modules/{code}/roster` and `/marks` (read-only, `AdminOnly`); the `/api/lecturer` routes stay lecturer-only. Administrators change a mark only through the audited correction route. | The registry checks who is on a module and what is about to be released every day, but a second write path into marks would blur who entered what. |

## 7. Conventions (apply everywhere)

- JSON: camelCase properties; enum values camelCase strings; instants ISO-8601 UTC with `Z`; times of day `"HH:mm"`;
  ids are UUID strings; paged lists are `{ items, page, pageSize, total }`.
- SQL: snake_case identifiers via `UseSnakeCaseNamingConvention()`; PascalCase string values for enum-like columns
  with CHECK constraints; `uuid` primary keys from `Guid.CreateVersion7()`; `timestamp with time zone` for instants.
- Routes: everything under `/api`; route constraints `{studentNumber:regex(^S[0-9]{{6}}$)}`, `{code:regex(^[A-Z]{{2}}[0-9]{{4}}$)}`,
  `{staffNumber:regex(^L[0-9]{{5}}$)}`, `{id:guid}` (digits `[0-9]`, never `\d`, `02-api.md` section 1).
- Errors: `application/problem+json`, `type` = `urn:rushday:<slug>`, `traceId` extension always set.
- Warnings are errors in every project; nullable on; `TimeProvider` injected, never `DateTimeOffset.UtcNow` in
  application code.
- Demo credentials: `S000001` / `Student-Demo-2026!`, `L00001` / `Lecturer-Demo-2026!`, `admin` / `Admin-Demo-2026!`.
- Local runs of the API: `scripts/run-api.ps1` only (never `dotnet run`, `dotnet watch run` or `dotnet run --no-build`
  in scripts, docs or Playwright configuration; CI may use `dotnet run --no-build` through `E2E_SERVER_COMMAND`).

## 8. Acceptance checklist: "production-ready" for a university IT buyer

Each item names how it is demonstrated. All are Must unless marked. The release stage (`06-implementation-plan.md`
stage S13) ticks this list in the pull request description.

**Story**
- [ ] The story sentence pair appears verbatim in the five locations of section 1 (grep in CI: `scripts/check-story.ps1`, run by the `build-and-test` job from S12 onward).

**Identity and access**
- [ ] No self-registration: `MapIdentityApi` is not referenced anywhere; integration test `POST /api/auth/register` → 404 ProblemDetails (the `/api/{**rest}` fallback is `AllowAnonymous`, so the answer is 404, not 401).
- [ ] Roles enforced server-side: `/api/me/*` requires `StudentOnly` and derives identity from claims; `/api/lecturer/*` requires `LecturerOnly` and `TeachesModule` where a module code appears; `/api/admin/*` requires `AdminOnly`. Integration tests cover each cross-role attempt → 403, including an administrator on lecturer routes.
- [ ] A student can never read another student's data: no student-role route takes a student number. Test: S000001's session on `/api/admin/students/S000002` → 403.
- [ ] Administrators use a TOTP second factor: an `Admin` without one can only reach `/api/auth/mfa/*` (403 `mfa-setup-required`); login with MFA answers `{ mfaRequired: true }` until the code is verified; an administrator can reset another account's factor; the demo administrator is exempt only while demo mode is on. Integration tests `AuthTests.Admin_without_mfa_is_gated`, `AuthTests.Mfa_login_round_trip`.
- [ ] Lockout after 5 failures for 15 minutes when the failures come from 3 or more addresses; a single address is stopped by the per-IP failure window (20 per 10 min) without locking the victim; login errors are generic and constant-time including the locked-out case; lockouts are audited (`auth.locked_out` with `ipHash`). `login-storm.js` in spray mode shows a correct login from a second address still succeeds.
- [ ] Sessions: `__Host-rushday.auth` is HttpOnly, Secure, SameSite=Strict; 8 h sliding / 12 h absolute (administrators 60 min / 8 h); security stamp validated every 5 minutes so disable, lock, lockout and password reset take effect; logout rotates the stamp so a replayed cookie is rejected.
- [ ] Antiforgery token required on every mutating `/api` request; test: POST without header → 400 `urn:rushday:antiforgery`.
- [ ] Forced password change for provisioned and reset accounts (`must_change_password`); every route that is not anonymous-capable answers 403 `password-change-required` until done, and the forced-change screen still renders its shell (test `AuthTests.Must_change_user_can_read_public_status`).
- [ ] Data Protection keys are encrypted at rest with `DataProtection:KeyEncryptionKey`; Production refuses to start without it.
- [ ] Should: Entra ID / SAML SSO documented as the Identity external-login extension point in `docs/admin-guide.md`.

**Data protection (marks are personal data under UK GDPR)**
- [ ] Unpublished marks unreachable by students: integration tests prove Draft, Submitted and future-Published marks are absent from `/api/me/dashboard` and `/api/me/results`, appear once a fake clock passes `published_at`, and never appear for a withdrawn enrolment (`GradeVisibilityTests.Withdrawn_student_never_sees_mark`).
- [ ] Results can be governed after release: a scheduled publication can be cancelled or a module pulled from it; a live one unpublished with a reason; a single mark corrected with a reason and shown to the student as "Amended" (`PublishTests`, `CorrectionTests`).
- [ ] Every enrolment, mark, publication and account change writes an `audit_events` row in the same transaction with actor, time, before/after and reason; the table is append-only at the database (trigger; test `AuditTests.Update_and_delete_are_rejected_by_the_database`); bulk reads (`audit.exported`, `student.viewed`, `student.exported_self`) are audited; export is CSV.
- [ ] Demo mode is a single switch (`Demo:Enabled`), off by default; `render.yaml` does not set it; with it off the login page shows no credentials, `GET /api/public/status` returns `demo: null`, and every `is_demo` account is disabled on the next start (test `MigrationOnSeededDatabaseTests.Demo_off_disables_every_demo_account`, `AuthTests.Demo_account_cannot_sign_in_when_demo_disabled`).
- [ ] A customer deployment path exists: `docs/deployment.md` "Deploying for a customer" lists the variables to leave unset and to set, the database-role and password-rotation procedures, and the data-residency statement.
- [ ] TLS only (Render terminates; app sets HSTS behind `ForwardedHeaders`, trusted only when `Security:TrustForwardedHeaders=true`); host filtering on; security headers per `03-security.md` section 4 on every response; no third-party scripts, fonts or connections on any page (CSP `connect-src 'self'`).
- [ ] Logs carry no names, marks, passwords or request bodies (student numbers, user ids and `ipHash` allowed).

**Correctness under load, with evidence**
- [ ] `load/k6/enrolment-rush.js` (500 authenticated students, 30 places): accepted = 30, `OVERSOLD=0`, no TCP refusals, the remaining 470 answered 409 `module-full` (the local limiter queue is sized so none are shed), every one under 50 ms. Before/after table in `docs/load-results/2026-10-xx-v1-hardened.md` next to the v0 numbers (154 accepted, 124 oversold, 69% failed).
- [ ] `load/k6/results-day.js` authenticated at 800 rps: `http_req_failed` < 1%, p95 < 500 ms, `rushday.dashboard.queries` = 5 per request (was ~15), measured by the EF command interceptor.
- [ ] `load/k6/dashboard-knee.js` beyond saturation: excess requests receive 503 with `Retry-After` within 1 s instead of multi-second failures; zero Postgres 53300 errors in the API log.
- [ ] `load/k6/login-storm.js` run with the production-strength CPU guard (`scripts/load.ps1 login-storm -ProductionLoginGuard`) documents the PBKDF2 cost at 210,000 iterations and the guard's 429 behaviour; the spray mode documents the per-IP failure window.
- [ ] Integration test: 200 parallel authenticated enrolments on a fresh 30-place module → exactly 30 × 201, 170 × 409, `enrolled_count = COUNT(*) = 30`.
- [ ] ADRs 0007 to 0012 written, each linking its before and after runs.

**Operability**
- [ ] `/api/health/live` is Render's health check; `/api/health/ready` reflects database reachability and is rate-limited; startup applies the migration and backfills idempotently and logs each step; a crash mid-backfill is recoverable by restart (integration test runs the backfills twice and asserts identical counts).
- [ ] Ops page shows, in plain language with the metric name as a caption, a health summary, live request rate, p95/p99, server errors, rate-limit and load-shed rejections, pool busy/idle/max and wait timeouts, memory; it keeps its last sample when the API is busy; alerting is out of scope on the free tier and the page says so.
- [ ] Memory during the 800 rps run stays below 350 MB working set (recorded in the load-results document).
- [ ] Structured JSON logs in Production with `traceId`, `userId`, `role`, route; `traceId` appears in every ProblemDetails so a user report maps to a log line.
- [ ] Backup and restore: Neon point-in-time restore documented in `docs/deployment.md` with the free plan's current restore window stated as measured and one rehearsed restore of the demo database recorded.
- [ ] Configuration is environment-only; no secrets in git or logs; `dotnet list package --vulnerable --include-transitive` and `npm audit --omit=dev --audit-level=high` clean in CI; GitHub secret scanning with push protection enabled; Dependabot security updates enabled; `main` protected by the `web`, `build-and-test` and `e2e` checks.

**Quality and accessibility**
- [ ] CI gates deploy (`autoDeployTrigger: checksPass`): `web` (lint, typecheck, unit tests, build, bundle budget), `build-and-test` (unit, Testcontainers integration, Docker image, story grep), `e2e` (Playwright journeys and axe) are all required checks.
- [ ] Playwright journeys green: sign in as each role; student enrols and withdraws; lecturer enters and submits marks; admin publishes, the student sees results, the admin corrects one and the student sees "Amended"; admin provisions an account and its first login forces a password change; a new administrator is forced through TOTP setup and then signs in with a code; admin disables an account and that session dies; guards and deep links; mobile viewport without horizontal scroll.
- [ ] WCAG 2.2 AA: axe (`wcag2a, wcag2aa, wcag22aa`) on every page in both themes with zero `serious` or `critical` violations; every status chip pair passes 4.5:1; keyboard-only path through login, enrol and marks entry; public `/accessibility` statement page.
- [ ] Documentation current: `README.md` (story, architecture, run, test, load, demo accounts), `docs/admin-guide.md` (including "re-run results day on the demo"), `docs/deployment.md`, ADRs, load results, this spec.
