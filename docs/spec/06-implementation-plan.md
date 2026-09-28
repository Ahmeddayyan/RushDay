# RushDay v1 specification: 06. Implementation plan

Scope: the ordered stages that build v1 from this specification, with explicit file ownership so coding agents never
edit the same file concurrently, a definition of done per stage, and how each stage is verified locally without Docker.

## 1. Rules for every stage

1. **Read first**: `docs/spec/00`–`05` in full, then the files the stage owns. Never ask; the spec decides. If the spec
   is genuinely silent, choose the smallest option consistent with the decision register and record it in the PR
   description under "Spec gaps".
2. **Ownership**: a stage creates, edits or deletes only the paths in its "Owns" list. Stages in the same row of the
   dependency graph run concurrently and have disjoint paths by construction. A stage that needs a change outside its
   list stops and reports it instead of making it.
3. **Green at the end of every stage**: `dotnet build RushDay.slnx -c Release` (warnings are errors) and
   `dotnet test tests/RushDay.UnitTests` for backend stages; `npm run lint`, `npm run typecheck`, `npm test` and
   `npm run build` in `src/RushDay.Web` for front-end stages; integration tests (`RUSHDAY_TEST_CONNECTION` path) from
   S2 onward for backend stages.
4. **Environment**: Windows 11, PowerShell or Git Bash; prepend
   `C:\Program Files\dotnet;C:\Program Files\nodejs;C:\Program Files\k6;C:\Program Files\GitHub CLI;` to `PATH` in every
   shell; `psql` is at `C:\Program Files\PostgreSQL\18\bin`. No Docker. .NET SDK 10.0.401, Node 24.19.0, k6 2.2.0,
   PostgreSQL 18 with role/database `rushday`/`rushday` on `localhost:5432`, already seeded at the v0 schema.
5. **Branching**: all stages land on branch `v1` through pull requests (CI runs on `pull_request`). `main` receives one
   merge after S12 is green, because Render deploys `main` on `checksPass` and the live demo must not sit for days
   between "old routes removed" and "SPA present". S13 then runs against the deployed demo and the local machine.
6. **Local database upgrade**: from S1 onward, `Demo__Enabled=true dotnet run --project src/RushDay.Api -- --migrate-and-seed`
   upgrades the existing local `rushday` database in place (migration + backfills), mirroring what Neon will do;
   `scripts/reset-db.ps1` rebuilds from empty when a clean state is needed.
7. **Integration tests without Docker**: set
   `RUSHDAY_TEST_CONNECTION=Host=localhost;Port=5432;Database=rushday_test;Username=rushday;Password=rushday`; the factory
   creates `rushday_test` (dropping it first), migrates, seeds 300 students with `SeedResultsDay = 2026-01-26T09:00:00Z`,
   runs the backfills with demo on, and drops the database at the end. CI leaves the variable unset and uses Testcontainers.
8. **Package versions** are fixed by `01`–`05`: `Microsoft.AspNetCore.Identity.EntityFrameworkCore` 10.0.12,
   `Microsoft.AspNetCore.DataProtection.EntityFrameworkCore` 10.0.12, `Microsoft.Extensions.Caching.Hybrid` 10.10.0,
   `Microsoft.Extensions.Diagnostics.Testing` 10.10.0, `Microsoft.Extensions.TimeProvider.Testing` 10.10.0 (tests only);
   npm additions as listed in `05-frontend.md` section 1.

## 2. Dependency graph

```
Row A   S1  Domain, schema, migration, backfills
Row B   S2  API foundation (auth, security, observability, hosting, public/auth endpoints, test factory)
Row C   S3  Web scaffold, hosting verification, Docker web stage, CI web job
Row D   S4  Student surface and load fixes (backend)      ∥   S5  Web core (design system, auth, shell, routing)
Row E   S6  Staff surface (backend)  ∥  S7 Student UI  ∥  S8 Lecturer UI  ∥  S9 Admin UI  ∥  S10 Ops and story UI
Row F   S11 Integration suite, k6, ADRs, docs             ∥   S12 Playwright, e2e script, CI e2e job
Row G   S13 Evidence run and release
```

S6 depends on S4 (services it reuses); S7–S10 depend on S5 and build against MSW handlers, so they do not wait for S6
to compile, but their pages only work end-to-end once S6 exists. S12 depends on S6–S10.

## 3. Stages

### S1. Domain, schema, migration, backfills

**Goal**: the model of `01-domain-and-data.md` compiles, migrates the populated local database in place, and the
backfills are idempotent.

**Owns**
- `src/RushDay.Domain/**` (all files listed in `01` section 4; keep existing files, extend them).
- `src/RushDay.Infrastructure/RushDay.Infrastructure.csproj` (add Identity EF, DataProtection EF, HybridCache packages).
- `src/RushDay.Infrastructure/Identity/{ApplicationUser,RushDayPasswordValidator,BlockedPasswords}.cs`.
- `src/RushDay.Infrastructure/Persistence/RushDayDbContext.cs`, `Persistence/Configurations/*.cs` (new and changed),
  `Persistence/Migrations/20261001120000_PortalAndIdentity.cs` (+ `.Designer.cs`, updated `RushDayDbContextModelSnapshot.cs`),
  `Persistence/DesignTimeDbContextFactory.cs` (unchanged unless the options type changes).
- `src/RushDay.Infrastructure/Seeding/{DatabaseSeeder,SeedOptions,StartupBackfills,DemoAccounts,LecturerSeed,DataBackfill}.cs`.
- `src/RushDay.Infrastructure/DependencyInjection.cs` (pool settings via `NpgsqlConnectionStringBuilder`, `Database:MaxPoolSize`; `AddRushDayPersistence(connectionString, maxPoolSize)`).
- `tests/RushDay.UnitTests/**` (extend `EnrolmentRulesTests` for `windowOpen` and the new order; add `EnrolmentWindowTests`, `RushDayPasswordValidatorTests`, `AuditActionsTests` (constants unique), `ClassificationTests` unchanged).
- `src/RushDay.Api/Program.cs` **one edit only**: pass `databaseOptions.MaxPoolSize` and call `StartupBackfills.RunAsync` after seeding when `BackfillOnStartup` or `--migrate-and-seed`; `src/RushDay.Api/DatabaseOptions.cs` gains `BackfillOnStartup`, `SeedResultsDay`, `MaxPoolSize`. (S2 later moves this file to `Options/`.)

**Definition of done**
- `dotnet ef migrations add PortalAndIdentity` produced exactly one new migration, hand-edited per `01` section 5
  (defaults added and dropped, SQL backfills for `department` and `enrolled_count`); `dotnet ef migrations script --idempotent` succeeds.
- `Demo__Enabled=true dotnet run --project src/RushDay.Api -- --migrate-and-seed` against a `TEMPLATE rushday` clone
  passes every assertion in `01` section 9 on two consecutive runs.
- `scripts/reset-db.ps1` converges from empty to the same counts (plus the seeder's own rows).
- Unit tests green; build green.

**Verify locally**: `01` section 9 steps 1–5.

### S2. API foundation

**Goal**: authentication, authorization, antiforgery, rate limiting, security headers, ProblemDetails, metrics
plumbing, SPA hosting, health, public and auth endpoints; legacy routes removed; test factory with auth helpers.

**Owns**
- `src/RushDay.Api/Program.cs` (thin: builder → `AddRushDayServices` → `StartupTasks.RunAsync` → `UseRushDayPipeline` → map endpoints → run), `Startup/{ServiceRegistration,PipelineConfiguration,StartupTasks,RequestLoggingMiddleware}.cs`.
- `src/RushDay.Api/Options/{DatabaseOptions,RateLimitingOptions,DemoOptions,BootstrapOptions,BrandingOptions,AuthOptions,SecurityOptions}.cs` (moves `DatabaseOptions.cs` from the project root).
- `src/RushDay.Api/Auth/{Policies,RushDayClaims,RushDayClaimsPrincipalFactory,RushDayCookieEvents,AntiforgeryEndpointFilter,MustChangePasswordFilter,TeachesModuleRequirement,TeachesModuleHandler,CurrentUser,LoginThrottle}.cs`.
- `src/RushDay.Api/Security/{SecurityHeadersMiddleware,RateLimitPolicies,ProblemTypes,ProblemResults,ProblemDetailsCustomizer}.cs`.
- `src/RushDay.Api/Observability/{RushDayMetrics,MetricsSnapshotService,MetricsSnapshot}.cs` (every instrument of `04` section 6.1 declared here; later stages only record).
- `src/RushDay.Api/Hosting/SpaHosting.cs`.
- `src/RushDay.Api/Endpoints/{IndexEndpoints,HealthEndpoints,PublicEndpoints,AuthEndpoints}.cs`; **deletes** `Endpoints/StudentEndpoints.cs`, `Endpoints/ModuleEndpoints.cs`, `wwwroot/index.html`, `Contracts/Dashboard.cs`, `Contracts/Enrolments.cs`, `Contracts/Modules.cs` (S4 recreates the contracts it needs).
- `src/RushDay.Api/Contracts/{Common,Auth,Public}.cs`.
- `src/RushDay.Infrastructure/Caching/{CacheKeys,EnrolmentWindowCache,SettingsCache,LecturerModuleCache,PublicationCache}.cs`, `Infrastructure/Accounts/AccountService.cs` (needed by login flow tests and by S6; provision/lock/unlock/disable/enable/reset/change-password), `Infrastructure/Audit/AuditWriter.cs`.
- `src/RushDay.Api/RushDay.Api.csproj` (GC settings; `Microsoft.Extensions.Caching.Hybrid` reference if not transitive), `appsettings.json`, `appsettings.Development.json`, `Properties/launchSettings.json`, `RushDay.Api.http`.
- `render.yaml` (`healthCheckPath: /api/health/live`, `Database__BackfillOnStartup=true`, `Demo__Enabled=true`, `Branding__InstitutionName`, `Branding__InstitutionShortName`, `Branding__TimeZone`).
- `tests/RushDay.IntegrationTests/**`: `RushDayApiFactory.cs` (Testcontainers or `RUSHDAY_TEST_CONNECTION`; seeds 300 students, runs backfills with demo on, `FakeTimeProvider` replacing `TimeProvider`, `Auth:SecurityStampIntervalMinutes=0`), `TestClients.cs` (`LoginAsync(username, password)` → `HttpClient` with cookie container and `X-CSRF-TOKEN` set from `Me.csrfToken`), `Auth/AuthTests.cs`, `Auth/AntiforgeryTests.cs`, `Auth/AuthorizationMatrixTests.cs` (asserting 403s against routes that S4/S6 will add is allowed: they are 404 until then, so the matrix test file is created here but the cases for `/api/me/*`, `/api/lecturer/*`, `/api/admin/*` are marked `Skip = "S4/S6"` and un-skipped by those stages), `Security/SecurityHeadersTests.cs`, `Security/RateLimitTests.cs`, `Endpoints/IndexEndpointTests.cs` (rewritten: `/api` links, `/` returns 503 text when `wwwroot/index.html` is absent and the SPA shell when present), `Endpoints/HealthEndpointTests.cs`, `Endpoints/PublicStatusTests.cs`, `Persistence/MigrationOnSeededDatabaseTests.cs`; **deletes** `Endpoints/DashboardEndpointTests.cs`, `Endpoints/EnrolmentEndpointTests.cs`.

**Definition of done**
- `POST /api/auth/csrf` → `login` → `me` → `change-password` → `logout` work with cookies and antiforgery; a POST without the header is 400 `antiforgery`; login lockout after 5 failures; disabled user session dies after stamp validation; `/api/auth/register` is 404 ProblemDetails; the fallback policy denies anonymous access to a probe endpoint.
- Headers, ProblemDetails (`traceId`, `urn:rushday:` types), request timeouts, ForwardedHeaders, HSTS (non-Development), rate limiting (429 with `Retry-After` at `LoginPerUserPerMinute=2` in a test), global concurrency limiter (503) are all covered by integration tests.
- `GET /api`, `/api/health/live`, `/api/health/ready`, `/api/public/status` (with and without demo), `/api/openapi/v1.json` respond as specified.
- `MetricsSnapshotService` aggregates `http.server.request.duration` and serves a snapshot (endpoint arrives in S6; the service is unit-tested for bucketing here).
- Build green; unit and integration tests green locally via `RUSHDAY_TEST_CONNECTION`.

**Verify locally**: `dotnet test tests/RushDay.IntegrationTests` with the env var; `dotnet run` and walk `RushDay.Api.http` (csrf → login → me).

### S3. Web scaffold, hosting verification, Docker, CI web job

**Goal**: the existing uncommitted `src/RushDay.Web` scaffold becomes the structure of `05-frontend.md` section 3
with placeholder routes, builds into `wwwroot`, is served by the API, and is built in Docker and CI.

**Owns**
- `src/RushDay.Web/**` except `src/features/{student,lecturer,admin,ops,story}/**` (S3 creates each area's `routes.tsx`
  exporting `[]` and nothing else there) and except `e2e/**`, `playwright.config.ts`, `tsconfig.e2e.json` (S12).
  Includes `package.json` (add the packages of `05` section 1, scripts), `package-lock.json`, `vite.config.ts`,
  `vitest.config.ts`, `tsconfig*.json`, `eslint.config.js`, `index.html`, `public/{favicon.svg,theme-init.js,robots.txt}`,
  `public/data/load-results.json` (initial v0-only file generated by a first version of `load/summarize.mjs`, see below),
  `scripts/bundle-budget.mjs`, `src/main.tsx`, `src/app/{providers,router,guards,AuthProvider,ErrorBoundary}.tsx` as
  compiling skeletons (full behaviour in S5), `src/api/{client,problem,keys,queryClient}.ts` skeletons, `src/styles/app.css`
  with the full token set, `src/lib/{cn,theme,useDocumentTitle}.ts`, `src/test/setup.ts`, a smoke test `src/app/router.test.tsx`.
- `load/summarize.mjs`, `load/results/runs.json` (v0 entries only), because the web build needs `public/data/load-results.json`.
- `.gitignore`, `.dockerignore`, `Dockerfile` (web stage per `05` section 14), `scripts/dev.ps1`.
- `.github/workflows/ci.yml`: add the `web` job and the `download-artifact web-dist` step before `build-and-test`'s restore;
  add `dotnet list package --vulnerable --include-transitive` and `npm audit --audit-level=high`.

**Definition of done**
- `npm ci && npm run lint && npm run typecheck && npm test && npm run build` succeed; `wwwroot` contains `index.html`,
  `theme-init.js`, `assets/*`, `data/load-results.json`; no inline `<script>` in the built `index.html`.
- `dotnet run` serves `/` (SPA shell), `/student/results` (deep link → shell), `/api/nope` (JSON 404), `/assets/*` with
  immutable caching; without `wwwroot/index.html`, `/` is the 503 text.
- Docker: cannot build locally (no Docker); the Dockerfile is validated by CI's `docker build` step.
- CI `web` job green on the PR.

**Verify locally**: build the web project, run the API in Release, check the four URLs above with `curl -i`.

### S4. Student surface and the load fixes (backend)

**Goal**: `/api/me/*`, `/api/modules*`, `/api/announcements` with the atomic enrolment, five-query dashboard, catalogue
cache, audit rows and metrics.

**Owns**
- `src/RushDay.Infrastructure/Enrolments/{EnrolmentService,EnrolmentWindowService}.cs`, `Infrastructure/Queries/{DashboardQuery,ResultsQuery,MyEnrolmentsQuery,ModuleDetailQuery}.cs`, `Infrastructure/Grades/GradeQueries.cs`, `Infrastructure/Caching/{CatalogueCache,AnnouncementCache}.cs`, `Infrastructure/Announcements/AnnouncementService.cs` (read for all roles; create/update/delete used by S6).
- `src/RushDay.Api/Endpoints/{MeEndpoints,ModuleEndpoints,AnnouncementEndpoints}.cs`, `Contracts/{Dashboard,Modules,Enrolments,Announcements}.cs`.
- `tests/RushDay.IntegrationTests/Student/**`: `DashboardTests` (shape; 5 queries via `MetricCollector<int>` on `rushday.dashboard.queries`), `GradeVisibilityTests` (draft, submitted, future-published hidden; visible after the fake clock passes), `EnrolmentTests` (201/404/409/422 matrix; window closed; withdrawal deadline; `results-exist`; reactivation), `EnrolmentConcurrencyTests` (200 parallel → 30), `CatalogueTests` (viewer-agnostic; cache hit serves without DB: assert via `rushday.cache.requests`), `AnnouncementsTests`. Un-skips the student cases of `AuthorizationMatrixTests`.

**Definition of done**: every row of `02` sections 8.2 and 8.3 implemented; the concurrency test passes; audit rows exist for enrol and withdraw; metrics recorded.

**Verify locally**: integration tests with `RUSHDAY_TEST_CONNECTION`; `dotnet run` + `RushDay.Api.http` enrol on CS3099 as `S000001`.

### S5. Web core: design system, auth, shell, routing

**Goal**: everything a feature page needs: `components/ui/*`, `components/layout/*`, `AuthProvider`, guards, client, problem
mapping, keys, router with lazy areas, login, account, password, announcements, forbidden, not-found, accessibility pages.

**Owns**
- `src/RushDay.Web/src/{app,api,components,lib,styles,test}/**` (completing the S3 skeletons; `api/endpoints/{auth,public,announcements}.ts`, `api/types/{auth,public,common}.ts`, `test/handlers/{auth,public}.ts`, `test/{render.tsx,server.ts,factories.ts}`).
- `src/RushDay.Web/src/features/{auth,shared}/**`.
- Tests of `05` section 13.1 for these files.

**Definition of done**: login/logout/forced-change/session-expiry flows work against the S2 API; guards redirect; theme persists with a throwing `localStorage`; `npm run build` under budget; axe clean on `/login`, `/account`, `/accessibility` (checked manually with the browser axe extension until S12 automates it).

### S6. Staff surface (backend)

**Goal**: `/api/lecturer/*` and `/api/admin/*` complete with audit rows, `TeachesModule` enforcement, ops snapshot and reconcile.

**Owns**
- `src/RushDay.Infrastructure/Grades/{MarksService,ResultsPublicationService}.cs`, `Infrastructure/Queries/{RosterQuery,MarksSheetQuery,AdminStudentQuery,AdminResultsQuery,AuditQuery,OverviewQuery}.cs`, `Infrastructure/Modules/ModuleAdminService.cs`, `Infrastructure/Settings/SettingsService.cs`, `Infrastructure/Enrolments/EnrolmentWindowAdminService.cs`, `Infrastructure/Students/StudentAdminService.cs`, `Infrastructure/Lecturers/LecturerAdminService.cs`, `Infrastructure/Audit/AuditCsvWriter.cs`, `Infrastructure/Ops/ReconcileService.cs`.
- `src/RushDay.Api/Endpoints/{LecturerEndpoints,AdminOverviewEndpoints,AdminSettingsEndpoints,AdminWindowEndpoints,AdminResultsEndpoints,AdminStudentEndpoints,AdminModuleEndpoints,AdminLecturerEndpoints,AdminAccountEndpoints,AdminAnnouncementEndpoints,AdminAuditEndpoints,AdminOpsEndpoints}.cs`, `Contracts/{Lecturer,AdminOverview,AdminSettings,AdminResults,AdminStudents,AdminModules,AdminAccounts,AdminAudit,AdminOps}.cs`.
- `tests/RushDay.IntegrationTests/Staff/**`: `MarksTests` (save all-or-nothing; stale-mark; not-enrolled-students; submit incomplete; locked after submit), `PublishTests` (submitted only; excluded drafts; future instant hidden then visible; reschedule; return to draft), `AdminOverrideTests` (window and credits ignored; capacity respected unless forced; reasons required), `AccountTests` (provision/lock/unlock/disable/enable/reset; self-lockout; must-change flow), `AuditTests` (every mutation writes a row; CSV export), `OpsTests` (snapshot shape; reconcile corrects drift). Un-skips the lecturer and admin cases of `AuthorizationMatrixTests`.

**Definition of done**: every row of `02` sections 8.4 and 8.5 implemented; tests green; a full lecturer → admin → student journey works through `RushDay.Api.http`.

### S7. Student UI

**Owns**: `src/RushDay.Web/src/features/student/**`, `src/api/endpoints/{student,modules}.ts`, `src/api/types/{student,modules}.ts`, `src/test/handlers/{student,modules}.ts`, tests of `05` section 13.1 for these.
**Done when**: dashboard, catalogue with optimistic enrol/withdraw, module detail, results, timetable and ICS all work against MSW and against the S4 API; mobile layout at 360 px has no horizontal scroll.

### S8. Lecturer UI

**Owns**: `src/RushDay.Web/src/features/lecturer/**`, `src/api/endpoints/lecturer.ts`, `src/api/types/lecturer.ts`, `src/test/handlers/lecturer.ts`, tests.
**Done when**: my modules, roster (paged, searched), marks grid (dirty tracking, keyboard, stale rows, submit dialog), module announcements work against MSW and the S6 API.

### S9. Admin UI

**Owns**: `src/RushDay.Web/src/features/admin/**`, `src/api/endpoints/admin.ts`, `src/api/types/admin.ts`, `src/test/handlers/admin.ts`, tests.
**Done when**: overview, students and support view, modules and assignment, lecturers, accounts and temporary-password flow, windows, results publication (publish, reschedule, return to draft), announcements, audit log with export, settings all work against MSW and the S6 API.

### S10. Ops and story UI

**Owns**: `src/RushDay.Web/src/features/{ops,story}/**`, `src/api/endpoints/ops.ts`, `src/api/types/{ops,loadResults}.ts`, `src/test/handlers/ops.ts`, tests; `load/summarize.mjs` and `load/results/runs.json` (refining the S3 first version; still v0 entries only until S13).
**Done when**: live tiles update every 5 s; pool meter; series charts; the three story chart cards and KPI rows render from the committed JSON with "not yet measured" for v1; table twins equal chart data; `/story` is public; Recharts is only in the `charts` chunk.

### S11. Integration suite completion, k6, ADRs, documentation

**Owns**
- `tests/RushDay.IntegrationTests/**` outside `Student/` and `Staff/` (add `Persistence/BackfillRecoveryTests` (kill after step 6 simulated by a failing step, restart completes), `Logging/LoggingRedactionTests`, `Security/CsvInjectionTests`), and any consolidation.
- `load/k6/**` (`lib/auth.js`, the three rewritten scenarios, `login-storm.js`), `load/README.md`, `scripts/{load,reset-db,seed,check-story}.ps1`.
- `docs/adr/0007`–`0012` (+ notes in 0003 and 0006), `docs/deployment.md` (env vars, `Bootstrap__AdminPassword` handling, Neon PITR restore procedure), `docs/admin-guide.md` (windows, publishing, overrides, accounts, demo switch, SSO extension point, retention), `README.md` (story first paragraph, architecture, run, front end, tests, load, demo accounts table, findings placeholder for S13), `src/RushDay.Api/RushDay.Api.http`.

**Done when**: `scripts/check-story.ps1` passes (five locations verbatim; no `dangerouslySetInnerHTML`); k6 scenarios authenticate and run against a local Release API (numbers not yet recorded); ADRs link the v0 runs and leave a clearly marked slot for the v1 run files.

### S12. Playwright, e2e script, CI e2e job

**Owns**: `src/RushDay.Web/e2e/**`, `src/RushDay.Web/playwright.config.ts`, `src/RushDay.Web/tsconfig.e2e.json`, `scripts/e2e.ps1`, `.github/workflows/ci.yml` (add the `e2e` job; `web` and `build-and-test` unchanged).
**Done when**: every journey of `05` section 13.2 is green locally (`scripts/e2e.ps1`) and in CI on both projects; axe reports zero serious/critical violations on every page in both themes.

### S13. Evidence run and release

**Owns**: `load/results/*.json` (new files), `load/results/runs.json` (v1 entries), `src/RushDay.Web/public/data/load-results.json` (regenerated), `docs/load-results/2026-10-xx-v1-hardened.md`, `README.md` ("Findings so far" section only), ADR 0007–0012 "after" links, `docs/deployment.md` (restore rehearsal record).

**Steps**
1. Merge `v1` into `main`; watch the Render deploy (`checksPass`); confirm the migration and backfills ran on Neon from the
   service log (nine backfill lines) and that `https://rushday-api.onrender.com/login` shows the demo accounts and
   `/api/public/status` reports `nextPublication`.
2. Run the procedure of `04` section 9 locally; commit the summaries and the regenerated JSON; write the load-results
   document; update README findings; fill the ADR "after" links.
3. Rehearse a Neon point-in-time restore of the demo branch to a new branch, confirm counts, record date and outcome in
   `docs/deployment.md`, delete the branch.
4. Tick the acceptance checklist of `00-overview.md` section 8 in the release PR description; anything unticked is listed
   with its follow-up issue.

## 4. Ownership matrix (quick lookup)

| Path | Stage |
|---|---|
| `src/RushDay.Domain/**` | S1 |
| `src/RushDay.Infrastructure/{Identity,Persistence,Seeding}/**`, `DependencyInjection.cs`, `.csproj` | S1 |
| `src/RushDay.Infrastructure/Caching/{CacheKeys,EnrolmentWindowCache,SettingsCache,LecturerModuleCache,PublicationCache}.cs`, `Accounts/**`, `Audit/AuditWriter.cs` | S2 |
| `src/RushDay.Infrastructure/{Enrolments/EnrolmentService,Enrolments/EnrolmentWindowService}.cs`, `Queries/{Dashboard,Results,MyEnrolments,ModuleDetail}Query.cs`, `Grades/GradeQueries.cs`, `Caching/{CatalogueCache,AnnouncementCache}.cs`, `Announcements/**` | S4 |
| `src/RushDay.Infrastructure/{Grades/MarksService,Grades/ResultsPublicationService}.cs`, `Queries/{Roster,MarksSheet,AdminStudent,AdminResults,Audit,Overview}Query.cs`, `Modules/**`, `Settings/**`, `Enrolments/EnrolmentWindowAdminService.cs`, `Students/**`, `Lecturers/**`, `Audit/AuditCsvWriter.cs`, `Ops/**` | S6 |
| `src/RushDay.Api/{Program.cs,Startup,Options,Auth,Security,Observability,Hosting}/**`, `Endpoints/{Index,Health,Public,Auth}Endpoints.cs`, `Contracts/{Common,Auth,Public}.cs`, `appsettings*.json`, `.csproj`, `RushDay.Api.http` (first pass), `render.yaml` | S2 |
| `src/RushDay.Api/Endpoints/{Me,Module,Announcement}Endpoints.cs`, `Contracts/{Dashboard,Modules,Enrolments,Announcements}.cs` | S4 |
| `src/RushDay.Api/Endpoints/{Lecturer,Admin*}Endpoints.cs`, `Contracts/{Lecturer,Admin*}.cs` | S6 |
| `tests/RushDay.UnitTests/**` | S1 |
| `tests/RushDay.IntegrationTests/**` (factory, Auth, Security, Endpoints, Persistence) | S2, completed by S11 |
| `tests/RushDay.IntegrationTests/Student/**` | S4 |
| `tests/RushDay.IntegrationTests/Staff/**` | S6 |
| `src/RushDay.Web/**` scaffold, configs, `scripts/bundle-budget.mjs`, `public/**` | S3 |
| `src/RushDay.Web/src/{app,api,components,lib,styles,test}/**`, `features/{auth,shared}/**` | S5 (skeletons by S3) |
| `src/RushDay.Web/src/features/student/**`, `api/{endpoints,types}/{student,modules}.ts`, `test/handlers/{student,modules}.ts` | S7 |
| `src/RushDay.Web/src/features/lecturer/**`, `api/{endpoints,types}/lecturer.ts`, `test/handlers/lecturer.ts` | S8 |
| `src/RushDay.Web/src/features/admin/**`, `api/{endpoints,types}/admin.ts`, `test/handlers/admin.ts` | S9 |
| `src/RushDay.Web/src/features/{ops,story}/**`, `api/endpoints/ops.ts`, `api/types/{ops,loadResults}.ts`, `test/handlers/ops.ts`, `load/summarize.mjs`, `load/results/runs.json` | S10 (first version S3) |
| `src/RushDay.Web/e2e/**`, `playwright.config.ts`, `tsconfig.e2e.json`, `scripts/e2e.ps1` | S12 |
| `load/k6/**`, `load/README.md`, `scripts/{load,reset-db,seed,check-story}.ps1`, `docs/adr/**`, `docs/deployment.md`, `docs/admin-guide.md`, `README.md` | S11 |
| `.gitignore`, `.dockerignore`, `Dockerfile`, `scripts/dev.ps1` | S3 |
| `.github/workflows/ci.yml` | S3 (`web` job), S12 (`e2e` job) |
| `load/results/*.json`, `docs/load-results/*v1*`, README findings, ADR after-links | S13 |
| `docs/spec/**` | frozen; changes only through a spec PR that names the affected stages |
