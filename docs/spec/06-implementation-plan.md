# RushDay v1 specification: 06. Implementation plan

Scope: the ordered stages that build v1 from this specification, with explicit file ownership so coding agents never
edit the same file concurrently, a definition of done per stage, how each stage is verified locally without Docker
and without `dotnet run`, and (section 5) what the already-implemented stage S1 must change to match this revision.

## 1. Rules for every stage

1. **Read first**: `docs/spec/00`–`05` in full, then this file's section for the stage, then the files the stage
   owns. Never ask; the spec decides. If the spec is genuinely silent, choose the smallest option consistent with the
   decision register and record it in the PR description under "Spec gaps".
2. **Ownership**: a stage creates, edits or deletes only the paths in its "Owns" list. Stages in the same row of the
   dependency graph run concurrently and have disjoint paths by construction. A stage that needs a change outside its
   list stops and reports it instead of making it.
3. **Green at the end of every stage**: `dotnet build RushDay.slnx -c Release` (warnings are errors) and
   `dotnet test tests/RushDay.UnitTests` for backend stages; `npm run lint`, `npm run typecheck`, `npm test` and
   `npm run build` in `src/RushDay.Web` for front-end stages; integration tests (`RUSHDAY_TEST_CONNECTION` path) from
   S2 onward for backend stages.
4. **Environment**: Windows 11, Windows PowerShell 5.1 (`powershell.exe`; `pwsh` may be absent, so every script is
   5.1-compatible) or Git Bash; prepend
   `C:\Program Files\dotnet;C:\Program Files\nodejs;C:\Program Files\k6;C:\Program Files\GitHub CLI;` to `PATH` in every
   shell; `psql` is at `C:\Program Files\PostgreSQL\18\bin`. No Docker. .NET SDK 10.0.401, Node 24.19.0, k6 2.2.0,
   PostgreSQL 18 with role/database `rushday`/`rushday` on `localhost:5432`, already seeded at the v0 schema.
5. **Running the API locally: never `dotnet run`.** Windows Smart App Control blocks `dotnet run` (and `dotnet watch
   run`) on freshly built unsigned assemblies on this machine (`FileLoadException 0x800711C7` on
   `RushDay.Infrastructure.dll`); `dotnet build`, `dotnet test` and `dotnet ef` work, and a framework-dependent
   single-file publish runs. Every local run of the API, in every script, doc and configuration, goes through
   `scripts/run-api.ps1` (owned by S1R, the first stage that must run the API):
   - `dotnet publish src/RushDay.Api -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -o
     $env:LOCALAPPDATA\RushDay\api` (skipped with `-NoPublish`; `-PublishOnly` publishes and exits);
   - then starts `$env:LOCALAPPDATA\RushDay\api\RushDay.Api.exe` in the foreground with the publish folder as its
     working directory (so `wwwroot` and `appsettings*.json` resolve), `ASPNETCORE_ENVIRONMENT=Development` unless the
     caller set it, `ASPNETCORE_URLS=http://localhost:5080` unless set, the caller's other environment variables
     inherited, and `-Args` passed through (for example `-Args "--migrate-and-seed"`); it exits with the exe's exit
     code.
   CI (Linux, no Smart App Control) may use `dotnet run --no-build`, only through `E2E_SERVER_COMMAND`.
6. **Branching**: all stages land on branch `v1` through pull requests (CI runs on `pull_request`). `main` receives one
   merge after S12 is green, because Render deploys `main` on `checksPass` and the live demo must not sit for days
   between "old routes removed" and "SPA present". S13 then runs against the deployed demo and the local machine.
7. **Local database upgrade**: from S1R onward,
   `$env:Demo__Enabled="true"; scripts/run-api.ps1 -Args "--migrate-and-seed"` upgrades the existing local `rushday`
   database in place (migration + backfills), mirroring what Neon will do; `scripts/reset-db.ps1` rebuilds from empty
   when a clean state is needed (it loses the 154 v0 CS3099 rows, so rehearse on a `TEMPLATE rushday` clone first,
   `01-domain-and-data.md` section 9).
8. **Integration tests without Docker**: set
   `RUSHDAY_TEST_CONNECTION=Host=localhost;Port=5432;Database=rushday_test;Username=rushday;Password=rushday`; the factory
   connects to the `postgres` maintenance database with the same credentials, runs `DROP DATABASE IF EXISTS rushday_test
   WITH (FORCE)` and `CREATE DATABASE rushday_test` (the role needs `CREATEDB`, which `scripts/db-create.ps1` grants),
   migrates, seeds 300 students with `SeedResultsDay = 2026-01-26T09:00:00Z`, runs the backfills with demo on, and drops
   the database at the end. It constructs `new FakeTimeProvider(new DateTimeOffset(2026, 9, 27, 12, 0, 0,
   TimeSpan.Zero))`, registers it as the `TimeProvider` singleton and exposes it as `factory.Clock`. A `FakeTimeProvider`
   cannot go back, so tests that advance the shared clock move it by **seconds only** (other tests assume the same day)
   and never "restore" it; a test that needs minutes or hours (session lifetimes, the 5-minute stamp interval, the MFA
   challenge, a publication instant) takes a host with a clock of its own, `factory.Derive(configure, clock: new
   FakeTimeProvider(...))`. `SignInManager`, the security-stamp validator, the cookie handler, the limiters of
   `LoginThrottle` and every application service resolve that `TimeProvider` from DI; **Identity's lockout
   (`AccessFailedAsync`, `IsLockedOutAsync`) and the TOTP check use the real clock**, so a lockout end is asserted
   against `DateTimeOffset.UtcNow` (about 15 minutes ahead) and TOTP codes are computed from the real time
   (`Totp.FreshCode`, which never repeats a step the server may have accepted). CI leaves the variable unset and uses
   Testcontainers.
9. **Package versions** are fixed by `01`–`05`: `Microsoft.AspNetCore.Identity.EntityFrameworkCore` 10.0.12,
   `Microsoft.AspNetCore.DataProtection.EntityFrameworkCore` 10.0.12, `Microsoft.Extensions.Caching.Hybrid` 10.10.0,
   `Microsoft.Extensions.Diagnostics.Testing` 10.10.0, `Microsoft.Extensions.TimeProvider.Testing` 10.10.0 (tests only);
   npm additions as listed in `05-frontend.md` section 1.

## 2. Dependency graph

```
Row 0   S0  Owner actions (not an agent): credential rotation, repository settings, optional v0 hotfix on main
Row A   S1  Domain, schema, migration, backfills (IMPLEMENTED, a73b447)  →  S1R  S1 conformance delta (section 5)
Row B   S2  API foundation (auth, MFA, security, observability, hosting, public/auth endpoints, test factory)
Row C   S3  Web scaffold, hosting verification, Docker, Dependabot, CI web job
Row D   S4  Student surface and load fixes (backend)      ∥   S5  Web core (design system, auth incl. MFA, shell, routing)
Row E   S6  Staff surface (backend)  ∥  S7 Student UI  ∥  S8 Lecturer UI  ∥  S9 Admin UI  ∥  S10 Ops and story UI
Row F   S11 Integration suite, k6, ADRs, docs, scripts    →   S12 Playwright, e2e script, CI e2e job and story check
Row G   S13 Evidence run and release
```

S1R runs alone before S2 and reuses S1's ownership. S6 depends on S4 (services and queries it reuses); S7–S10 depend
on S5 and build against MSW handlers, so they do not wait for S6 to compile, but their pages only work end-to-end once
S6 exists. S12 may be developed alongside S11 (disjoint paths) but its PR merges after S11's, because the story check
it adds to CI needs S11's README and ADR 0007. S12 also depends on S6–S10. S0 items marked "now" happen immediately;
the rest are S13's pre-merge checklist.

## 3. Stages

### S0. Owner actions (not an agent; no code on `v1`)

- **Now**: rotate the Neon password that was handled in chat during the first deploy (Neon console → reset the owner
  role's password → update `ConnectionStrings__RushDay` in the Render dashboard → redeploy → confirm the v0 `/health`
  answers); note the date for `docs/deployment.md`'s rotation log.
- **Now**: in the GitHub repository settings enable secret scanning with push protection and Dependabot security
  updates (both free).
- **Now, optional** (finding: the live v0 accepts unauthenticated writes until v1 ships): a separate branch
  `hotfix/v0-writes` off `main` and a PR to `main` that makes v0's `POST /students/{n}/enrolments` answer 404 unless
  `Demo__LegacyWrites=true` (a v0-only setting, unset on Render; v1 has no such key) and adds one README line "v0
  is live and intentionally unauthenticated on synthetic data until v1 ships." Owns, on `main` only: `src/RushDay.Api/Endpoints/StudentEndpoints.cs`, `README.md`.
  When `v1` later merges into `main`, resolve both conflicts by taking `v1` (S2 deletes the file; S11 rewrites the
  README).
- **Before S13** (S13 step 0): create the `rushday_app` role and set the Render environment (`03-security.md` section
  8), then protect `main`.

### S1. Domain, schema, migration, backfills (IMPLEMENTED in `a73b447`)

**Goal**: the model of `01-domain-and-data.md` compiles, migrates the populated local database in place, and the
backfills are idempotent. Implemented against the pre-critique draft; section 5 lists what it must change, which
stage S1R does.

**Owns** (S1R reuses this list)
- `src/RushDay.Domain/**` (all files listed in `01` section 4; keep existing files, extend them).
- `src/RushDay.Infrastructure/RushDay.Infrastructure.csproj` (packages; the embedded blocklist resource).
- `src/RushDay.Infrastructure/Identity/{ApplicationUser,PasswordHashing,RushDayPasswordValidator,BlockedPasswords}.cs`,
  `Identity/blocked-passwords.txt`.
- `src/RushDay.Infrastructure/Persistence/RushDayDbContext.cs`, `Persistence/Configurations/*.cs`,
  `Persistence/Migrations/20261001120000_PortalAndIdentity.cs` (+ `.Designer.cs`, `RushDayDbContextModelSnapshot.cs`),
  `Persistence/DesignTimeDbContextFactory.cs`.
- `src/RushDay.Infrastructure/Seeding/{DatabaseSeeder,SeedOptions,StartupBackfills,StartupBackfillOptions,DemoAccounts,LecturerSeed,DataBackfill}.cs`.
- `src/RushDay.Infrastructure/DependencyInjection.cs` (pool settings via `NpgsqlConnectionStringBuilder`,
  `AddRushDayPersistence(connectionString, maxPoolSize)`).
- `tests/RushDay.UnitTests/**` except `Observability/**` (S2), and `tests/RushDay.UnitTests/RushDay.UnitTests.csproj`
  (S1 added the Infrastructure reference; S2 adds the Api reference).
- `src/RushDay.Api/Program.cs` and `src/RushDay.Api/DatabaseOptions.cs`: S1's one block (migrate → seed → backfills,
  `MaxPoolSize`, `BackfillOnStartup`, `SeedResultsDay`); S2 then replaces `Program.cs` and moves `DatabaseOptions.cs`.
  S1 also edited `tests/RushDay.IntegrationTests/RushDayApiFactory.cs` for `RUSHDAY_TEST_CONNECTION`; S2 owns that file
  from here on.

**Definition of done** (re-checked by S1R)
- Exactly one new migration, `20261001120000_PortalAndIdentity`, hand-edited per `01` section 5; `dotnet ef migrations
  script --idempotent` succeeds; `dotnet ef migrations has-pending-model-changes` reports none.
- `$env:Demo__Enabled="true"; scripts/run-api.ps1 -Args "--migrate-and-seed"` against a `TEMPLATE rushday` clone passes
  every assertion in `01` section 9 on two consecutive runs, and once more with demo off.
- `scripts/reset-db.ps1` converges from empty to the same counts (plus the seeder's own rows).
- Unit tests green; build green.

**Verify locally**: `01` section 9 steps 1–5.

### S1R. S1 conformance

**Goal**: the committed S1 code conforms to this revision: every item of section 5, and nothing else; plus the
local-run scripts the rehearsal needs: `scripts/run-api.ps1` (new, section 1 rule 5); `scripts/reset-db.ps1` and
`scripts/seed.ps1`, whose last line becomes `scripts/run-api.ps1 -Args "--migrate-and-seed"` instead of `dotnet run`
(nothing else changes); `scripts/db-create.ps1`, which adds `ALTER ROLE rushday CREATEDB`.
**Owns**: the S1 list above and `scripts/{run-api,reset-db,seed,db-create}.ps1`. **Done when**: S1's definition of
done holds against the revised `01`, including the new assertions of `01` section 9 step 3, and the unit tests of
section 5.9 pass.

### S2. API foundation

**Goal**: authentication with the TOTP second factor, authorization, antiforgery, rate limiting and the login
throttle, security headers, host filtering and forwarded headers, the encrypted key ring, ProblemDetails, metrics
plumbing and the command counter, SPA hosting, health, public and auth endpoints; legacy routes removed; test
factory with auth helpers and the fake clock.

**Owns**
- `src/RushDay.Api/Program.cs` (thin: builder → `AddRushDayServices` → `StartupTasks.RunAsync` → `UseRushDayPipeline` → map endpoints → run), `Startup/{ServiceRegistration,PipelineConfiguration,StartupTasks,RequestLoggingMiddleware}.cs`. `StartupTasks` implements the demo guard, the KEK check, migration on `ConnectionStrings:Migrations` when set, the seeder only in demo mode or under `--migrate-and-seed`, and the backfills (`01` section 6, `04` section 7).
- `src/RushDay.Api/Options/{DatabaseOptions,RateLimitingOptions,DemoOptions,BootstrapOptions,BrandingOptions,AuthOptions,SecurityOptions,DataProtectionOptions}.cs` (moves `DatabaseOptions.cs` from the project root).
- `src/RushDay.Api/Auth/{Policies,RushDayClaims,RushDayClaimsPrincipalFactory,RushDayCookieEvents,RushDaySecurityStampValidator,AntiforgeryEndpointFilter,MustChangePasswordFilter,MfaSetupRequiredFilter,TeachesModuleRequirement,TeachesModuleHandler,CurrentUser,HttpAuditContext,LoginThrottle,SlidingWindowCounter,MfaChallengeBinding,ReplayProtectedAuthenticatorTokenProvider}.cs` (the last three from the S2 review).
- `src/RushDay.Api/Security/{SecurityHeadersMiddleware,RateLimitPolicies,ProblemTypes,ProblemResults,ProblemDetailsCustomizer,AesGcmXmlEncryptor,AesGcmXmlDecryptor,DataProtectionKeyEncryptionKey,IpHasher,HostFilteringSetup,KeyRingHygiene}.cs`, `src/RushDay.Api/Startup/RushDayDbContextFactory.cs` (the singleton `IDbContextFactory<RushDayDbContext>` every cache factory uses, 04 section 4).
- `src/RushDay.Api/Observability/{RushDayMetrics,MetricsSnapshotService,MetricsSnapshot,DbCommandCounter}.cs` (every instrument of `04` section 6.1 declared here; later stages only record).
- `src/RushDay.Api/Hosting/SpaHosting.cs` (both fallbacks and the 503 endpoint `AllowAnonymous`).
- `src/RushDay.Api/Endpoints/{IndexEndpoints,HealthEndpoints,PublicEndpoints,AuthEndpoints}.cs` (`AuthEndpoints` includes the `/api/auth/mfa/*` routes of `02` section 2.4); **deletes** `Endpoints/StudentEndpoints.cs`, `Endpoints/ModuleEndpoints.cs`, `Contracts/Dashboard.cs`, `Contracts/Enrolments.cs`, `Contracts/Modules.cs` (S4 recreates the contracts it needs), and the working-tree-only `wwwroot/index.html`.
- `src/RushDay.Api/Contracts/{Common,Auth,Public}.cs`.
- `src/RushDay.Infrastructure/Caching/{CacheKeys,EnrolmentWindowCache,SettingsCache,LecturerModuleCache,PublicationCache}.cs`, `Infrastructure/Accounts/AccountService.cs` (provision, lock, unlock, disable, enable, reset password, reset MFA, change password, the `demo-account` guard; used by the login flow tests and by S6), `Infrastructure/Audit/{AuditWriter,IAuditContext,AuditHashes}.cs` (`IAuditContext` supplies actor, whether the actor is a demo account, request id and `ipHash`; `Api/Auth/HttpAuditContext` implements it).
- `src/RushDay.Api/RushDay.Api.csproj` (GC settings), `appsettings.json`, `appsettings.Development.json` (the Development column of `03` section 8, including `Security:TrustForwardedHeaders=true` and the relaxed `RateLimiting`), `Properties/launchSettings.json`, `RushDay.Api.http` (first pass).
- `render.yaml`: `healthCheckPath: /api/health/live`, `autoDeployTrigger: checksPass`, `region: frankfurt`, env vars `ASPNETCORE_ENVIRONMENT=Production`, `ASPNETCORE_HTTP_PORTS=8080`, `PORT=8080`, `Database__MigrateOnStartup=true`, `Database__BackfillOnStartup=true`, `Security__TrustForwardedHeaders=true`, `Branding__TimeZone=Europe/London`, and `sync: false` for `ConnectionStrings__RushDay`, `ConnectionStrings__Migrations`, `DataProtection__KeyEncryptionKey`, `Bootstrap__AdminPassword`, `Branding__InstitutionName`, `Branding__InstitutionShortName`. **No** `Demo__*` and **no** `Database__Seed*` keys (D29); a comment says the public demo sets `Demo__Enabled` and `Demo__PublicDemoAcknowledged` in the Render dashboard only.
- `tests/RushDay.UnitTests/Observability/**` (`MetricsSnapshotService` bucketing and percentiles, `DbCommandCounter`), and in `tests/RushDay.UnitTests/RushDay.UnitTests.csproj` only the added `ProjectReference` to `RushDay.Api` and `PackageReference` `Microsoft.Extensions.Diagnostics.Testing` 10.10.0.
- `tests/RushDay.IntegrationTests/**`: `RushDayApiFactory.cs` (section 1 rule 8; `Auth:SecurityStampIntervalMinutes=0`; a Production-mode variant with a fixed test KEK for header, cookie-name and key-ring tests), `TestClients.cs` (`LoginAsync(username, password, forwardedFor?)` → `HttpClient` with cookie container and `X-CSRF-TOKEN` from `Me.csrfToken`; an MFA-aware variant), `Auth/{AuthTests,AntiforgeryTests,AuthorizationMatrixTests}.cs` (the matrix file is created here; cases for `/api/me/*`, `/api/lecturer/*`, `/api/admin/*` are marked `Skip = "S4/S6"` and un-skipped by those stages), `Security/{SecurityHeadersTests,RateLimitTests,HostFilteringTests,DataProtectionTests,StartupTests,ProblemTypesTests}.cs`, `Endpoints/{IndexEndpointTests,HealthEndpointTests,PublicStatusTests,FactoryClockTests}.cs`; **deletes** `Endpoints/DashboardEndpointTests.cs`, `Endpoints/EnrolmentEndpointTests.cs`.

**Definition of done**
- `GET /api/auth/csrf` → `POST /api/auth/login` → `GET /api/auth/me` → `change-password` → `logout` work with cookies
  and antiforgery; a POST without the header is 400 `antiforgery`; `/api/auth/register` is 404 ProblemDetails
  anonymously.
- Login: lockout after 5 failures from 3 addresses (`X-Forwarded-For`); a single-address spray is 429 without locking
  the victim; unknown, wrong-password, locked-out, disabled and demo-disabled answers are indistinguishable; demo
  accounts never lock; a disabled or locked-out user's session dies after stamp validation; a cookie replayed after
  logout is rejected; the forced-change user can read `/api/public/status`.
- MFA: `AuthTests.Mfa_login_round_trip` and `AuthTests.Admin_without_mfa_is_gated` pass; the demo admin is exempt only
  in demo mode.
- Anonymous `GET /` → 200 `text/html` (or the 503 text without a build) and anonymous `GET /api/nope` → 404 JSON.
- Headers (CSP, HSTS outside Development, `__Host-` cookie names in the Production-mode factory), host filtering,
  ProblemDetails (`traceId`, `urn:rushday:` types, the 62-slug `ProblemTypes` set), request timeouts, forwarded headers,
  rate limiting (429 with `Retry-After` at `LoginPerUserPerMinute=2` in a test), the global limiter (503), the
  `health-ready` limiter, the encrypted key ring (`DataProtectionTests.Key_xml_is_not_plaintext`), and the startup
  aborts (`StartupTests.Production_demo_without_acknowledgement_aborts`, `StartupTests.Production_without_kek_aborts`)
  are covered by integration tests.
- `GET /api`, `/api/health/live`, `/api/health/ready`, `/api/public/status` (with and without demo) and
  `/api/openapi/v1.json` (Development only) respond as specified; `FactoryClockTests` asserts `serverTime` starts with
  `2026-09-27`.
- `MetricsSnapshotService` aggregates `http.server.request.duration` and serves a snapshot (the endpoint arrives in
  S6; the service and `DbCommandCounter` are unit-tested here).
- Build green; unit and integration tests green locally via `RUSHDAY_TEST_CONNECTION`.

**Verify locally**: `dotnet test tests/RushDay.IntegrationTests` with the env var; `scripts/run-api.ps1` and walk
`RushDay.Api.http` (csrf → login → me).

### S3. Web scaffold, hosting verification, Docker, CI web job

**Goal**: the existing committed `src/RushDay.Web` scaffold becomes the structure of `05-frontend.md` section 3 with
placeholder routes, builds into `wwwroot`, is served by the API, and is built in Docker and CI.

**Owns**
- `src/RushDay.Web/**` except `src/features/{student,lecturer,admin,ops,story}/**` (S3 creates each of those areas'
  `routes.tsx` exporting `[]` and nothing else there), except the content of `src/features/{auth,shared}/**` (S3 moves
  the scaffold's files out of `features/auth` and `features/theme` and leaves compiling placeholder pages and
  `routes.tsx` files there; S5 owns them afterwards), and except `e2e/**`, `tests/e2e/**`,
  `playwright.config.ts`, `tsconfig.e2e.json` (S12). Includes `package.json` (add the packages of `05` section 1,
  scripts), `package-lock.json`, `vite.config.ts`, `vitest.config.ts`, `tsconfig{,.app,.node}.json`, `eslint.config.js`,
  `index.html`, `public/{favicon.svg,theme-init.js,robots.txt}`, `public/data/load-results.json` (initial v0-only file
  generated by a first version of `load/summarize.mjs`, see below), `scripts/bundle-budget.mjs`, `src/main.tsx`,
  `src/app/{providers,router,guards,AuthProvider,ErrorBoundary}.tsx` as compiling skeletons (full behaviour in S5;
  `router.tsx` final, with the seven static `routes.tsx` imports), `src/api/{client,problem,keys,queryClient}.ts`
  skeletons, `src/styles/app.css` with the full token set of `05` section 9.1, `src/lib/{cn,theme,useDocumentTitle}.ts`,
  `src/test/setup.ts`, a smoke test `src/app/router.test.tsx`; the file migrations of `05` section 3.
- `load/summarize.mjs`, `load/results/runs.json` (v0 entries only), because the web build needs
  `public/data/load-results.json`.
- `.gitignore`, `.dockerignore`, `Dockerfile` (`05` section 14), `.github/dependabot.yml`, `scripts/dev.ps1`.
- `.github/workflows/ci.yml`: split the current single job into `web` and `build-and-test` per `05` section 14 (actions
  pinned by SHA; `download-artifact web-dist` before `build-and-test`'s restore; `dotnet list package --vulnerable
  --include-transitive`; `npm audit --omit=dev --audit-level=high` plus the non-blocking full audit). **No** story check
  yet (S12 adds it).

**Definition of done**
- `npm ci && npm run lint && npm run typecheck && npm test && npm run build && npm run bundle-budget` succeed; `wwwroot`
  contains `index.html`, `theme-init.js`, `assets/*`, `data/load-results.json`, `.vite/manifest.json`; no inline
  `<script>` in the built `index.html`.
- `scripts/run-api.ps1` serves `/` (SPA shell), `/student/results` (deep link → shell), `/api/nope` (JSON 404),
  `/assets/*` with immutable caching; without `wwwroot/index.html`, `/` is the 503 text.
- Docker: cannot build locally (no Docker); the Dockerfile is validated by CI's `docker build` step.
- CI `web` job green on the PR.

**Verify locally**: build the web project, `scripts/run-api.ps1`, check the four URLs above with `curl -i`.

### S4. Student surface and the load fixes (backend)

**Goal**: `/api/me/*`, `/api/modules*`, `/api/announcements` with the atomic enrolment, year-scoped reads, the
five-query dashboard, the catalogue cache, the visibility rule, the personal export, audit rows and metrics.

**Hooks and patterns S2 provides** (S4 edits none of S2's files):
- Services: implement `static partial void AddStudentSurface(IServiceCollection services)` of `Startup/ServiceRegistration.cs`
  in a file of its own (`public static partial class ServiceRegistration { static partial void AddStudentSurface(IServiceCollection services) { ... } }`,
  namespace `RushDay.Api.Startup`), for example `src/RushDay.Api/Startup/ServiceRegistration.Student.cs`; routes:
  implement `static partial void MapStudentSurface(RouteGroupBuilder api)` of `Startup/PipelineConfiguration.cs` the
  same way (`Startup/PipelineConfiguration.Student.cs`), mapping onto the `/api` group so the antiforgery filter and the
  gates apply. Both files are S4's.
- Caches: `CatalogueCache` and `AnnouncementCache` take `IDbContextFactory<RushDayDbContext>` (registered by S2) and
  open their own context inside the factory (`await using var db = await contexts.CreateDbContextAsync(ct)`), never
  the caller's scoped context, and read through `CacheKeys.GetOrCreateAsync` so the fill is not counted as the
  request's commands (04 section 4).
- `DashboardTests` measure the handler's queries only: `DbCommandCounter` starts after authorization and skips cache
  fills, so the metric is 5 whether or not the request re-checked the stamp or filled a cache; the log cross-check uses
  `factory.DeriveWithCommandLog()`, a warm-up call, `GetFakeLogCollector().Clear()` and a second call without moving the
  clock (04 section 6.1).

**Owns**
- `src/RushDay.Infrastructure/Enrolments/{EnrolmentService,EnrolmentWindowService}.cs`, `Infrastructure/Queries/{DashboardQuery,ResultsQuery,TimetableQuery,MyEnrolmentsQuery,ModuleDetailQuery,StudentExportQuery}.cs`, `Infrastructure/Grades/GradeQueries.cs`, `Infrastructure/Caching/{CatalogueCache,AnnouncementCache}.cs`, `Infrastructure/Announcements/AnnouncementService.cs` (read for all roles; create/update/delete used by S6).
- `src/RushDay.Api/Endpoints/{MeEndpoints,ModuleEndpoints,AnnouncementEndpoints}.cs` (including `GET /api/me/export.json`), `Contracts/{Dashboard,Modules,Enrolments,Announcements,Export}.cs`.
- `tests/RushDay.IntegrationTests/Student/**`: `DashboardTests` (shape incl. `completed` and `currentSemester`; `rushday.dashboard.queries` = 5 via `MetricCollector<int>` fed by `DbCommandCounter`, plus 5 captured `Microsoft.EntityFrameworkCore.Database.Command` log entries), `GradeVisibilityTests` (draft, submitted, future-published hidden; visible after `factory.Clock` passes `published_at`; `Withdrawn_student_never_sees_mark`), `EnrolmentTests` (201/404/409/422 matrix with `S000001`–`S000050` and CS3099; `module-inactive`; window closed with the three extension shapes; withdrawal deadline; `results-exist`; reactivation of a withdrawn row and of an earlier-year row stamps the current year; credit limit with `S000002` and five autumn modules), `EnrolmentConcurrencyTests` (`ZZ3001` created through `RushDayDbContext`; `S000101`–`S000300`; 30 × 201, 170 × 409, `enrolled_count = count = 30`), `CatalogueTests` (viewer-agnostic; a cache hit serves without the database: assert via `rushday.cache.requests`; inactive module detail returns `isActive: false`), `TimetableTests` (current semester only; `kind` from the room), `ExportTests` (shape without drafts; audit `student.exported_self`), `AnnouncementsTests`. Un-skips the student cases of `AuthorizationMatrixTests`.

**Definition of done**: every row of `02` sections 8.2 and 8.3 implemented; the concurrency test passes; audit rows
exist for enrol, withdraw and export; metrics recorded.

**Verify locally**: integration tests with `RUSHDAY_TEST_CONNECTION`; `scripts/run-api.ps1` + `RushDay.Api.http`
enrol on CS3099 as `S000001`.

### S5. Web core: design system, auth, shell, routing

**Goal**: everything a feature page needs: `components/ui/*`, `components/layout/*`, `AuthProvider` with the MFA and
re-authentication flows, guards, client, problem mapping, keys, router, login (with the MFA step), account, password,
MFA setup, announcements, forbidden, not-found and accessibility pages.

**Owns**
- `src/RushDay.Web/src/{app,api,components,lib,styles,test}/**` (completing the S3 skeletons; `app/ReauthDialog.tsx`; `api/endpoints/{auth,public,announcements}.ts`, `api/types/{auth,public,common}.ts`, `test/handlers/{auth,public}.ts`, `test/{render.tsx,server.ts,factories.ts}`; `lib/{format,moduleCode,returnTo,useDebouncedValue,useCountdown,useDirtyForm,download,broadcast,qr,contrast}.ts`).
- `src/RushDay.Web/src/features/{auth,shared}/**`.
- The S5 tests of `05` section 13.1.

**Definition of done**: login (including the MFA code step), logout, forced change, forced MFA setup, session expiry
with `ReauthDialog` while a form is dirty, all against the S2 API; guards redirect; theme persists with a throwing
`localStorage`; `lib/contrast.test.ts` passes; `npm run build` under budget; axe clean on `/login`, `/account`,
`/account/mfa`, `/accessibility` (checked manually with the browser axe extension until S12 automates it).

### S6. Staff surface (backend)

**Goal**: `/api/lecturer/*` and `/api/admin/*` complete with audit rows, `TeachesModule` and leader enforcement, the
results lifecycle (publish, reschedule, cancel, unpublish, return to draft, correct), student and lecturer editing and
leaving, module reads for the registry, the ops snapshot, reconcile and the demo reset.

**Hooks and patterns S2 provides** (S6 edits none of S2's files):
- Services: implement `static partial void AddStaffSurface(IServiceCollection services)` of `Startup/ServiceRegistration.cs`
  in a file of its own (for example `src/RushDay.Api/Startup/ServiceRegistration.Staff.cs`); routes: implement
  `static partial void MapStaffSurface(RouteGroupBuilder api)` of `Startup/PipelineConfiguration.cs` the same way
  (`Startup/PipelineConfiguration.Staff.cs`), mapping `/lecturer` and `/admin` groups onto the `/api` group. Both files
  are S6's.
- Accounts go through S2's `AccountService`, which already refuses a demo actor on a real account (409 `demo-account`)
  and makes what a demo actor provisions a demo account (02 section 8.5); `AccountTests` cover it through the real
  routes.
- `OpsTests` expect `dataQuality.refreshedAt` only from the second snapshot on: the first poll after an idle spell
  starts the refresh (04 section 6.2).

**From the S4 review** (04 sections 2.2 and 2.3): trim-to-capacity and leave withdraw through
`EnrolmentService.WithdrawManyAsync` once, inside the route's transaction (never a loop of `WithdrawAsync`, and
without locking module rows first); the reconcile route and the settings year change call
`StartupBackfills.ReconcileEnrolledCountAsync` (the year change after its settings `UPDATE`), never the two statements
on their own; module mutations invalidate `catalogue:all` and settings changes `settings` after the commit.

**From the S6 review** (recorded after the fix pass; 02 sections 1, 4, 6, 7, 8.3-8.5, 01 sections 3, 5a, 6, 9, 03 section
7, 04 sections 2-4 and 7):
- No stranded mark (E1): `MarksStatus.entered` counts, once a module has left draft, only Submitted or Published
  grades, so a Draft on an active enrolment is `missing` and the module is not publishable; nobody joins a module whose
  marks for the year have left draft (409 `module-locked`, self or override, under the module's marks lock taken
  shared); a publish takes its candidates' marks locks and re-reads their status. Chosen over "reopen the student's
  marking" as the smallest rule that keeps every mark reachable (return to draft, then enrol).
- Publications (E2, E3, E9): `results_publications.announcement_id` links the "results are available" announcement,
  which a reschedule moves and a cancel, an unpublish or an emptying return to draft deletes; an emptied scheduled
  publication is deleted (`results.cancelled` with `returnedToDraft`); return to draft locks publication rows before
  grade rows. A correction that changes nothing is 400 `validation` (E12).
- Registry (E4-E8, E10, J1): trim counts real enrolments under the students' locks and repairs the counter; lecturers
  who have left have no authority and, like students who have left, get no account (409 `principal-left`); a demo
  actor cannot rename a real login through a record edit; the semester is pinned by any enrolment or grade of any year;
  a leave keeps enrolments that hold results; a unique index allows one leader; trim and leave call
  `WithdrawManyAsync`.
- Platform (E11, E13-E16, J2-J5): null list elements, integer enum values and pages beyond row 10,000 are 400
  `validation`; the roster CSV is audited (`roster.exported`); `ANALYZE` after a start that migrated or backfilled; the
  reconcile route's pre-lock is `FOR NO KEY UPDATE`; `AnnouncementWriteScope` replaces the nullable module id; the MFA
  code is `^[0-9]{6}$`; windows, publications and lecturer-module caches are generation-versioned. J6 (the admin
  export's web type) belongs to the UI pass; J7 (the Kelvin sign) is accepted and documented.
- Schema: the additive migration `20261002120000_ResultsGovernance` (01 section 5a); `PortalAndIdentity` is unchanged.
- Tests: `Staff/ResultsGovernanceTests`, `RegistryRulesTests`, `StaffConcurrencyTests`, `PlatformFixTests` and the unit
  `Grades/MarksStatusTests`, each failing on 93a2029 for the reason it names (except the leave race, which that base
  already ordered correctly).

**Owns**
- `src/RushDay.Infrastructure/Grades/{MarksService,ResultsPublicationService}.cs` (publication, reschedule, cancel, unpublish, return to draft, correction), `Infrastructure/Queries/{RosterQuery,MarksSheetQuery,AdminStudentQuery,AdminResultsQuery,AuditQuery,OverviewQuery}.cs`, `Infrastructure/Modules/ModuleAdminService.cs` (create, update with the capacity and semester guards, lecturers, trim), `Infrastructure/Settings/SettingsService.cs` (year change reconciles), `Infrastructure/Enrolments/EnrolmentWindowAdminService.cs`, `Infrastructure/Students/StudentAdminService.cs` (create, update, leave), `Infrastructure/Lecturers/LecturerAdminService.cs` (create, update, leave), `Infrastructure/Audit/AuditCsvWriter.cs`, `Infrastructure/Ops/{ReconcileService,DemoResetService}.cs`.
- `src/RushDay.Api/Endpoints/{LecturerEndpoints,AdminOverviewEndpoints,AdminSettingsEndpoints,AdminWindowEndpoints,AdminResultsEndpoints,AdminStudentEndpoints,AdminModuleEndpoints,AdminLecturerEndpoints,AdminAccountEndpoints,AdminAnnouncementEndpoints,AdminAuditEndpoints,AdminOpsEndpoints}.cs` (`AdminModuleEndpoints` includes the read-only roster and marks routes; `AdminOpsEndpoints` maps `demo-reset` only when `Demo:Enabled`), `Contracts/{Lecturer,AdminOverview,AdminSettings,AdminResults,AdminStudents,AdminModules,AdminLecturers,AdminAccounts,AdminAudit,AdminOps}.cs`.
- `tests/RushDay.IntegrationTests/Staff/**`: `MarksTests` (save all-or-nothing per request; `stale-mark`; `not-enrolled-students`; outcomes and the mark/outcome rule; submit incomplete; `nothing-to-submit`; a teacher gets `not-module-leader`; withdrawn students' drafts stay Draft; locked after submit), `PublishTests` (publishable modules only; excluded reasons; `Submitted_module_with_missing_mark_is_excluded_and_not_published`; future instant hidden then visible with `factory.Clock`; reschedule; cancel; unpublish; `Return_to_draft_allowed_before_scheduled_instant`; `module-locked` once live; announce creates the pinned announcement), `CorrectionTests` (a corrected published mark is visible at once with `correctedAt`; a draft answers `module-not-submitted`; audit before/after), `AdminOverrideTests` (window and credits ignored; capacity respected unless forced; capacity raised only when full; reasons required; `module-inactive`), `AdminModuleTests` (capacity rule incl. unchanged capacity on an oversold module; `semester-change-with-enrolments`; trim; roster and marks read routes), `StudentAdminTests` (edit syncs `display_name`; leave withdraws and disables), `SettingsTests` (a year change recomputes `enrolled_count`), `AccountTests` (provision/lock/unlock/disable/enable/reset password/reset MFA; self-lockout; must-change flow; `Demo_showcase_accounts_cannot_be_altered`), `AuditTests` (`Every_mutation_writes_a_row`; CSV export with truncation header; `Export_writes_an_audit_row`; `Student_view_writes_an_audit_row`; `Update_and_delete_are_rejected_by_the_database`), `OpsTests` (snapshot shape; reconcile corrects drift; `demo-reset` mapped only in demo mode). Un-skips the lecturer and admin cases of `AuthorizationMatrixTests` (including `Teacher_cannot_submit_marks`, `Lecturer_cannot_edit_announcement_of_another_module`, `Lecturer_cannot_delete_university_announcement`, `Admin_gated_by_mfa_cannot_publish`, and an administrator on `GET /api/lecturer/modules` and `PUT /api/lecturer/modules/CS3099/marks` → 403).

**Definition of done**: every row of `02` sections 8.4 and 8.5 implemented; tests green; a full lecturer → admin →
student journey (enter, submit, publish, correct, see "Amended") works through `RushDay.Api.http`.

### S7. Student UI

**Owns**: `src/RushDay.Web/src/features/student/**`, `src/api/endpoints/{student,modules}.ts`, `src/api/types/{student,modules}.ts`, `src/test/handlers/{student,modules}.ts`, the S7 tests of `05` section 13.1.
**Done when**: dashboard (current and completed modules, results release at the instant), catalogue with the
non-optimistic enrol and every `EnrolButton` state, optimistic withdraw, module detail, results by year and semester,
timetable (current semester, table semantics) and ICS all work against MSW and against the S4 API; mobile layout at
360 px has no horizontal scroll.

### S8. Lecturer UI

**Owns**: `src/RushDay.Web/src/features/lecturer/**`, `src/api/endpoints/lecturer.ts`, `src/api/types/lecturer.ts`, `src/test/handlers/lecturer.ts`, the S8 tests.
**Done when**: my modules, roster (paged, searched), marks grid (paged, outcomes, chunked save, dirty tracking across
pages, keyboard, stale rows, session-expiry mirror, leader-only submit dialog), module announcements work against MSW
and the S6 API.

### S9. Admin UI

**Owns**: `src/RushDay.Web/src/features/admin/**`, `src/api/endpoints/admin.ts`, `src/api/types/admin.ts`, `src/test/handlers/admin.ts`, the S9 tests.
**Done when**: overview, students (create with provisioning, edit, mark as left, export, correct mark) and the support
view, modules (edit guards, assignment, trim, read-only roster and marks), lecturers (edit, mark as left), accounts
(provision, temporary password, lock chips, reset MFA, demo rows read-only), windows, results (publish with the
exam-board confirmation, reschedule, cancel, unpublish, return to draft, excluded reasons), announcements, audit log
with export, settings all work against MSW and the S6 API.

### S10. Ops and story UI

**Owns**: `src/RushDay.Web/src/features/{ops,story}/**`, `src/api/endpoints/ops.ts`, `src/api/types/{ops,loadResults}.ts`, `src/test/handlers/ops.ts`, the S10 tests; `load/summarize.mjs` and `load/results/runs.json` (refining the S3 first version; still v0 entries only until S13).
**Done when**: the health summary and plain-language tiles update every 5 s and keep the last sample when a poll
fails; pool meter; series charts with the empty state; data checks with re-count and (demo) reset; technical details
collapsed; the three story chart cards, glossary and machine banner render from the committed JSON with "not yet
measured" for v1; table twins equal chart data; `/story` is public and contains the story literal; Recharts is only in
the `charts` chunk.

### S11. Integration suite completion, k6, ADRs, documentation, scripts

**Owns**
- `tests/RushDay.IntegrationTests/**` outside `Student/` and `Staff/` and outside the files S2 created:
  `Persistence/{MigrationOnSeededDatabaseTests,BackfillRecoveryTests}.cs` (`01` section 9 step 6; a failing step
  simulated after backfill step 6 and a restart that completes), `Logging/LoggingRedactionTests.cs`,
  `Security/CsvInjectionTests.cs`, and any consolidation of S2's files that keeps their test names.
- `load/k6/**` (`lib/auth.js`, the three rewritten scenarios, `login-storm.js` with guard and spray modes),
  `load/README.md`, `scripts/{load,check-story}.ps1`: `load.ps1` gains `login-storm`, `-Rushers`, `-LoginPool`,
  `-Mode` and `-ProductionLoginGuard` and starts the API through `scripts/run-api.ps1` (`04` section 8);
  `check-story.ps1` greps the sentence pair verbatim in `README.md`,
  `src/RushDay.Api/Endpoints/IndexEndpoints.cs`, `src/RushDay.Web/src/features/auth/LoginPage.tsx`,
  `src/RushDay.Web/src/features/story/StoryPage.tsx`, `src/RushDay.Web/index.html` and
  `docs/adr/0007-identity-and-cookie-sessions.md`, and fails on any `dangerouslySetInnerHTML` under
  `src/RushDay.Web/src`.
- `docs/adr/0007`–`0012` (+ notes in 0003 and 0006), `docs/deployment.md` (every variable of `03` section 8; the
  "Deploying for a customer" section; the `rushday_app` role script and the residual risk when one role is kept; the
  "Rotate the database password" procedure and rotation log; the KEK generation command; the Neon resume note of `04`
  section 5; Neon point-in-time restore with a slot for the measured window; the data-residency statement; the
  repository settings: secret scanning, push protection, Dependabot, branch protection), `docs/admin-guide.md`
  (windows; publishing, cancelling, unpublishing, return to draft and corrections; "Re-run results day on the demo":
  as `L00001` enter and submit CS3001's marks, then as `admin` publish Autumn 2026/27 at a future instant and watch the
  login page and `S000001`'s dashboard count down and refresh; unpublish and return to draft to reset; overrides and
  trim; accounts and MFA reset; the demo switch and what turning it off does; `/story` is public on every deployment
  and holds only RushDay's own load-test evidence; SSO extension point; retention), `README.md` (story first paragraph,
  architecture, run with `scripts/run-api.ps1`, front end, tests, load, demo accounts table, findings placeholder for
  S13), `src/RushDay.Api/RushDay.Api.http` (complete).

**Done when**: `scripts/check-story.ps1` passes locally; k6 scenarios authenticate and run against the local API
started with `scripts/run-api.ps1` (numbers not yet recorded); ADRs link the v0 runs and leave a clearly marked slot
for the v1 run files.

### S12. Playwright, e2e script, CI e2e job and story check

**Owns**: `src/RushDay.Web/e2e/**`, `src/RushDay.Web/playwright.config.ts`, `src/RushDay.Web/tsconfig.e2e.json`,
`src/RushDay.Web/tests/e2e/**` (moved into `e2e/`, then deleted), `scripts/e2e.ps1`, `.github/workflows/ci.yml` (add
the `e2e` job of `05` section 14 and the `pwsh scripts/check-story.ps1` step at the end of `build-and-test`; the rest
of `web` and `build-and-test` unchanged).
**Done when**: every journey of `05` section 13.2 is green locally (`scripts/e2e.ps1`) and in CI on the projects each
runs on; axe reports zero serious/critical violations on every page in both themes; the story step is green. Merges
after S11.

### S13. Evidence run and release

**Owns**: `load/results/*.json` (new files), `load/results/runs.json` (v1 entries), `src/RushDay.Web/public/data/load-results.json` (regenerated), `docs/load-results/2026-10-xx-v1-hardened.md`, `README.md` ("Findings so far" section only), ADR 0007–0012 "after" links, `docs/deployment.md` (restore rehearsal record and measured restore window).

**Steps**
0. Pre-merge checklist (owner, S0): create `rushday_app` on Neon with the grants of `03` section 8; set in the Render
   dashboard `ConnectionStrings__RushDay` (the `rushday_app` string with `SSL Mode=VerifyFull;Channel Binding=Require`),
   `ConnectionStrings__Migrations` (the owner string), `DataProtection__KeyEncryptionKey` (32 random bytes, base64),
   `Demo__Enabled=true` and `Demo__PublicDemoAcknowledged=true`; after the Blueprint sync, confirm those are present and
   that `Database__SeedOnStartup` and `Database__SeedStudentCount` are gone (the Neon database is already seeded); protect
   `main` with the required checks `web`, `build-and-test` and `e2e`.
1. Merge `v1` into `main`; watch the Render deploy (`checksPass`); confirm from the service log that the migration and
   twelve backfill lines ran on Neon (demo on: steps 1–8 and 10–13) and the `DEMO MODE` warning is present; confirm
   `https://rushday-api.onrender.com/login` shows the demo accounts and the published results line, and
   `/api/public/status` reports `latestPublication.state = "live"` (`nextPublication` is null unless someone has
   scheduled a publication).
2. Run the procedure of `04` section 9 locally; commit the summaries and the regenerated JSON; write the load-results
   document; update README findings; fill the ADR "after" links.
3. Rehearse a Neon point-in-time restore of the demo branch to a new branch, confirm counts, record the date, the
   outcome and the restore window the free plan offers that day in `docs/deployment.md`, delete the branch.
4. Tick the acceptance checklist of `00-overview.md` section 8 in the release PR description; anything unticked is listed
   with its follow-up issue.

## 4. Ownership matrix (quick lookup)

| Path | Stage |
|---|---|
| `src/RushDay.Domain/**` | S1 → S1R |
| `src/RushDay.Infrastructure/{Identity,Persistence,Seeding}/**`, `DependencyInjection.cs`, `.csproj` | S1 → S1R |
| `src/RushDay.Infrastructure/Caching/{CacheKeys,EnrolmentWindowCache,SettingsCache,LecturerModuleCache,PublicationCache}.cs`, `Accounts/**`, `Audit/{AuditWriter,IAuditContext,AuditHashes}.cs` | S2 |
| `src/RushDay.Infrastructure/{Enrolments/EnrolmentService,Enrolments/EnrolmentWindowService}.cs`, `Queries/{Dashboard,Results,Timetable,MyEnrolments,ModuleDetail,StudentExport}Query.cs`, `Grades/GradeQueries.cs`, `Caching/{CatalogueCache,AnnouncementCache}.cs`, `Announcements/**` | S4 |
| `src/RushDay.Infrastructure/{Grades/MarksService,Grades/ResultsPublicationService}.cs`, `Queries/{Roster,MarksSheet,AdminStudent,AdminResults,Audit,Overview}Query.cs`, `Modules/**`, `Settings/**`, `Enrolments/EnrolmentWindowAdminService.cs`, `Students/**`, `Lecturers/**`, `Audit/AuditCsvWriter.cs`, `Ops/**` | S6 |
| `src/RushDay.Api/Program.cs`, `DatabaseOptions.cs` | S1 (one block) → S2 |
| `src/RushDay.Api/{Startup,Options,Auth,Security,Observability,Hosting}/**` (except the S4/S6 hook files below), `Endpoints/{Index,Health,Public,Auth}Endpoints.cs`, `Contracts/{Common,Auth,Public}.cs`, `appsettings*.json`, `Properties/launchSettings.json`, `.csproj`, `RushDay.Api.http` (first pass), `render.yaml` | S2 |
| `scripts/{run-api,reset-db,seed,db-create}.ps1` | S1R |
| `src/RushDay.Api/Endpoints/{Me,Module,Announcement}Endpoints.cs`, `Contracts/{Dashboard,Modules,Enrolments,Announcements,Export}.cs`, `Startup/{ServiceRegistration,PipelineConfiguration}.Student.cs` (the S2 hooks) | S4 |
| `src/RushDay.Api/Endpoints/{Lecturer,Admin*}Endpoints.cs`, `Contracts/{Lecturer,Admin*}.cs`, `Startup/{ServiceRegistration,PipelineConfiguration}.Staff.cs` (the S2 hooks) | S6 |
| `tests/RushDay.UnitTests/**` except S2's folders; the csproj except S2's references | S1 → S1R |
| `tests/RushDay.UnitTests/{Observability,Auth,Security,Contracts,Startup}/**`; the csproj's `RushDay.Api`, `Diagnostics.Testing` and `TimeProvider.Testing` references | S2 |
| `tests/RushDay.IntegrationTests/**` (factory, `TestClients`, `Auth`, `Security`, `Endpoints`, `Caching`) | S2 (S1 touched the factory) |
| `tests/RushDay.IntegrationTests/Persistence/**`, `Logging/**`, `Security/CsvInjectionTests.cs` | S11 |
| `tests/RushDay.IntegrationTests/Student/**` | S4 |
| `tests/RushDay.IntegrationTests/Staff/**` | S6 |
| `src/RushDay.Web/**` scaffold, configs, `scripts/bundle-budget.mjs`, `public/**`, `src/app/router.tsx` | S3 |
| `src/RushDay.Web/src/{app,api,components,lib,styles,test}/**` (except `router.tsx`), `features/{auth,shared}/**` | S5 (skeletons by S3) |
| `src/RushDay.Web/src/features/student/**`, `api/{endpoints,types}/{student,modules}.ts`, `test/handlers/{student,modules}.ts` | S7 |
| `src/RushDay.Web/src/features/lecturer/**`, `api/{endpoints,types}/lecturer.ts`, `test/handlers/lecturer.ts` | S8 |
| `src/RushDay.Web/src/features/admin/**`, `api/{endpoints,types}/admin.ts`, `test/handlers/admin.ts` | S9 |
| `src/RushDay.Web/src/features/{ops,story}/**`, `api/endpoints/ops.ts`, `api/types/{ops,loadResults}.ts`, `test/handlers/ops.ts`, `load/summarize.mjs`, `load/results/runs.json` | S10 (first version S3) |
| `src/RushDay.Web/{e2e,tests/e2e}/**`, `playwright.config.ts`, `tsconfig.e2e.json`, `scripts/e2e.ps1` | S12 |
| `load/k6/**`, `load/README.md`, `scripts/{load,check-story}.ps1`, `docs/adr/**`, `docs/deployment.md`, `docs/admin-guide.md`, `README.md`, `RushDay.Api.http` (complete) | S11 |
| `.gitignore`, `.dockerignore`, `Dockerfile`, `.github/dependabot.yml`, `scripts/dev.ps1` | S3 |
| `.github/workflows/ci.yml` | S3 (`web`, `build-and-test`), S12 (`e2e` job, story step) |
| `load/results/*.json`, `docs/load-results/*v1*`, README findings, ADR after-links, `docs/deployment.md` restore record | S13 |
| `main`-only v0 hotfix (`Endpoints/StudentEndpoints.cs`, `README.md` on `main`) | S0 (optional) |
| `docs/spec/**` | frozen; changes only through a spec PR that names the affected stages |

## 5. S1 conformance delta

Stage S1 (`a73b447`) was implemented against the pre-critique draft. This is the complete list of changes S1R makes so
that it conforms to the revised `01`–`04`; anything not listed already conforms (section 5.10). Paths are relative to
`src/RushDay.Infrastructure` unless stated.

### 5.1 How the migration is changed: edit `PortalAndIdentity` in place

`20261001120000_PortalAndIdentity` has never been applied to a shared database (Neon still runs `InitialCreate` only;
only local throwaway clones were upgraded), so S1R **replaces it in place** rather than adding a second migration:

1. For every local database that has it applied (`SELECT "MigrationId" FROM "__EFMigrationsHistory"`): drop the
   throwaway clones; if the main local `rushday` database was upgraded, roll it back **before** changing any code with
   `dotnet ef database update 20260927150750_InitialCreate -p src/RushDay.Infrastructure -s src/RushDay.Api` (S1's `Down`
   preserves the v0 data, including the 154 CS3099 rows).
2. Make the model changes of sections 5.2 and 5.3.
3. `dotnet ef migrations remove -p src/RushDay.Infrastructure -s src/RushDay.Api` (reverts the snapshot), then
   `dotnet ef migrations add PortalAndIdentity ...`, rename the two files to `20261001120000_PortalAndIdentity.cs` /
   `.Designer.cs` and set `[Migration("20261001120000_PortalAndIdentity")]`.
4. Re-apply the hand edits, using S1's file (`git show a73b447:src/RushDay.Infrastructure/Persistence/Migrations/20261001120000_PortalAndIdentity.cs`)
   as the template for its step comments and SQL, changed as in section 5.4.
5. `dotnet ef migrations has-pending-model-changes` reports none; `01` section 9 passes on a fresh `TEMPLATE rushday`
   clone.

### 5.2 Domain (`src/RushDay.Domain`)

1. `Students/Student.cs`: `FullName`, `Programme`, `YearOfStudy` become `{ get; set; }`; add `DateTimeOffset? LeftAt
   { get; set; }`.
2. `Lecturers/Lecturer.cs`: add `DateTimeOffset? LeftAt { get; set; }`.
3. `Enrolments/Enrolment.cs`: add `required string AcademicYear { get; set; }` (settable: reactivation re-stamps it).
4. `Enrolments/EnrolmentRules.cs`: `Evaluate(Module module, int currentEnrolledCount, int studentCreditsInSemester, bool
   alreadyEnrolled, bool windowOpen, bool ignoreCreditLimit = false)`; the credit check runs only when
   `!ignoreCreditLimit`; order unchanged (`AlreadyEnrolled`, `WindowClosed`, `CreditLimitExceeded`, `ModuleFull`,
   `Accepted`).
5. `Grades/GradeOutcome.cs` (new): `enum GradeOutcome { Mark, Absent, Deferred }`.
6. `Grades/Grade.cs`: `int Mark` → `int? Mark`; add `GradeOutcome Outcome { get; set; }` and `DateTimeOffset?
   CorrectedAt { get; set; }`.
7. `Grades/Classification.cs`: add `Graded(IEnumerable<(GradeOutcome Outcome, int? Mark, int Credits)>)` returning
   the `(int Mark, int Credits)` pairs with `Outcome == Mark`, and `Band(int mark) => FromAverage(mark)`;
   `WeightedAverage` and `FromAverage` unchanged.
8. `Settings/AcademicSettings.cs`: add `Semester CurrentSemester { get; set; }`, `string? SupportEmail { get; set; }`,
   `string? SupportUrl { get; set; }`.
9. `Audit/AuditSubjects.cs` (new): string constants `Enrolment`, `Grade`, `Module`, `Publication`, `Announcement`,
   `Account`, `Settings`, `Window`, `Student`, `Lecturer`, `System`, plus `All`.
10. `Audit/AuditActions.cs`: add the 17 missing constants so the catalogue equals `03` section 7 exactly (48 actions):
    `AccountMfaSetupStarted = "account.mfa_setup_started"`, `AccountMfaEnabled = "account.mfa_enabled"`,
    `AccountMfaReset = "account.mfa_reset"`, `GradeCorrected = "grade.corrected"`, `ResultsCancelled =
    "results.cancelled"`, `ResultsUnpublished = "results.unpublished"`, `ModuleTrimmed = "module.trimmed"`,
    `StudentUpdated = "student.updated"`, `StudentLeft = "student.left"`, `StudentViewed = "student.viewed"`,
    `StudentExported = "student.exported"`, `StudentExportedSelf = "student.exported_self"`, `LecturerUpdated =
    "lecturer.updated"`, `LecturerLeft = "lecturer.left"`, `AuditExported = "audit.exported"`, `SystemDemoReset =
    "system.demo_reset"`, `SystemDemoAccountsDisabled = "system.demo_accounts_disabled"`; add `SubjectOf(string action)`:
    `auth.*` and `account.*` → `Account`, `enrolment.*` → `Enrolment`, `grade.*` → `Grade`, `module.*` → `Module`,
    `results.*` → `Publication`, `announcement.*` → `Announcement`, `settings.*` → `Settings`, `window.*` → `Window`,
    `student.*` → `Student`, `lecturer.*` → `Lecturer`, `audit.*`, `ops.*`, `system.*` → `System`; unknown → throws.
11. `Audit/AuditEvent.cs`: add `string? ChainHash { get; init; }` (Should; always null in v1).

### 5.3 Persistence model (`Persistence/Configurations`, `Identity`)

1. `EnrolmentConfiguration`: `AcademicYear` `HasMaxLength(9).IsRequired()`; `Source` gains
   `.HasDefaultValue(EnrolmentSource.Seed)` (the default is **kept**, `01` section 3); new index
   `HasIndex(e => new { e.StudentId, e.AcademicYear, e.Status })` named `ix_enrolments_student_id_academic_year_status`.
2. `GradeConfiguration`: `Outcome` `HasConversion<string>().HasMaxLength(16).IsRequired().HasDefaultValue(GradeOutcome.Mark)`;
   CHECK `ck_grades_outcome` = `outcome IN ('Mark', 'Absent', 'Deferred')`; CHECK `ck_grades_mark_range` changes to
   `mark IS NULL OR (mark >= 0 AND mark <= 100)`; new CHECK `ck_grades_mark_outcome` = `(outcome = 'Mark') = (mark IS NOT
   NULL)`; `mark` becomes nullable through the CLR type; `corrected_at` needs no configuration.
3. `AcademicSettingsConfiguration`: CHECK `ck_academic_settings_current_semester` = `current_semester IN (1, 2)`;
   `SupportEmail` `HasMaxLength(256)`; `SupportUrl` `HasMaxLength(400)`; `CurrentSemester` stays an `integer` like
   `modules.semester` (no string conversion).
4. `AuditEventConfiguration`: `ChainHash` `HasMaxLength(64)`.
5. `StudentConfiguration`, `LecturerConfiguration`: nothing beyond the new nullable `left_at` columns.
6. `Identity/PasswordHashing.cs` (new): `public const int IterationCount = 210_000;` and `public static
   PasswordHasher<ApplicationUser> Create() => new(Options.Create(new PasswordHasherOptions { IterationCount =
   IterationCount }));`.
7. `Identity/BlockedPasswords.cs`: becomes a `FrozenSet<string>` (case-insensitive) loaded once from the embedded
   resource `Identity/blocked-passwords.txt` (`<EmbeddedResource Include="Identity\blocked-passwords.txt"
   LogicalName="RushDay.Infrastructure.Identity.blocked-passwords.txt" />` in the csproj), generated as `01` section 8
   says and containing S1's current hand-written entries too.
8. `Identity/RushDayPasswordValidator.cs`: add `public static IReadOnlyList<string> Check(string username, string
   password)` returning the failing codes of the whole policy (length 12..128, at least 4 distinct characters,
   username, `rushday`, blocklist); `ValidateAsync` reuses the username, product-name and blocklist rule methods.

### 5.4 Migration operations (S1's step order is kept; the revised `01` section 5 now describes it)

S1 ordered `Up` as Identity tables → domain tables → `students` → `modules` → `enrolments` → `grades` → foreign keys,
with the reconciliation right after `enrolments.status`. `01` section 5 adopts that order. Changes within it:

1. Step 2: `lecturers` gains `left_at`; `academic_settings` gains `current_semester integer NOT NULL`, `support_email
   varchar(256) NULL`, `support_url varchar(400) NULL` and `ck_academic_settings_current_semester`; `audit_events`
   gains `chain_hash varchar(64) NULL`; immediately after `audit_events` is created,
   `migrationBuilder.Sql(...)` creates the function `audit_events_immutable()` and the trigger
   `trg_audit_events_immutable` exactly as in `01` section 3.
2. Step 3 (`students`): also add `left_at`.
3. Step 5 (`enrolments`): **delete** `ALTER TABLE enrolments ALTER COLUMN source DROP DEFAULT` (the `'Seed'` default is
   kept); add `academic_year varchar(9) NOT NULL DEFAULT '2025/26'` followed by `ALTER TABLE enrolments ALTER COLUMN
   academic_year DROP DEFAULT`; create `ix_enrolments_student_id_academic_year_status`; **delete** the two
   `migrationBuilder.Sql(Seeding.StartupBackfills.Reconcile...Sql)` statements (the migration no longer reconciles; the
   count is year-scoped and computed by the last backfill step).
4. Step 6 (`grades`): `AlterColumn<int>("mark", nullable: true)`; add `outcome varchar(16) NOT NULL DEFAULT 'Mark'`
   (default kept) and `corrected_at timestamptz NULL`; replace `ck_grades_mark_range` with the nullable form; add
   `ck_grades_outcome` and `ck_grades_mark_outcome`.
5. `Down`: drop the trigger and the function before `audit_events` (`DROP TRIGGER IF EXISTS trg_audit_events_immutable ON
   audit_events; DROP FUNCTION IF EXISTS audit_events_immutable();`); drop the new CHECKs, index and columns; run `UPDATE
   grades SET mark = 0 WHERE mark IS NULL` before restoring `mark NOT NULL` (and keep S1's `published_at` handling).
6. Designer and snapshot regenerated (section 5.1); the snapshot has no default for `academic_year` and has
   `HasDefaultValue` for `enrolments.source` and `grades.outcome`.

### 5.5 Startup backfills (`Seeding/StartupBackfills.cs`)

1. **Order and modes** become those of `01` section 6: `roles` (always) → `academic_settings` (once) →
   `results_publication_autumn_2025_26` (once) → `relabel_v0_self_enrolments` (once, new) → `admin_account` (always) →
   `enrolment_windows_2026_27` (**always, demo**; was once, every mode) → `lecturers_and_assignments` (**once, demo**;
   was once, every mode) → `demo_accounts` (always, demo) → `demo_accounts_disable` (always, demo off, new) →
   `demo_announcements` (once, demo) → `demo_reset_hot_module` (always, demo, new) → `demo_autumn_cohort` (once, demo,
   new) → `reconcile_enrolled_count` (always, **last**; was first). `StepNames` gains `RelabelV0SelfEnrolments`,
   `DemoAccountsDisable`, `DemoResetHotModule`, `DemoAutumnCohort`.
2. `ReconcileEnrolledCountSql` and `ReconcileZeroEnrolledCountSql` become the year-scoped statements of `01` step 13
   (joined to `academic_settings`); `ReconcileEnrolledCountAsync(db, ct)` stays public (S6's reconcile and settings
   services call it).
3. `academic_settings`: also sets `CurrentSemester = Semester.Autumn`, `SupportEmail = null`, `SupportUrl = null`.
4. `enrolment_windows_2026_27`: the `INSERT ... ON CONFLICT (academic_year, semester) DO UPDATE ... WHERE ... IS DISTINCT
   FROM ...` upsert of `01` step 6 for both windows, then the academic-year heal; rows affected = rows inserted or
   restored.
5. `relabel_v0_self_enrolments` (new): the `UPDATE` of `01` step 4 (sets `source = 'Self'` and `academic_year =
   '2026/27'`).
6. `admin_account`: "exists" becomes the **usable** test of `01` step 5 (role `Admin`, `disabled_at IS NULL`, and `NOT
   is_demo OR DemoEnabled`) instead of "any Admin role row"; the username comes from
   `options.BootstrapAdminUsername` (was the `DemoAccounts.AdminUsername` constant); a taken username logs the Warning
   and skips; a bootstrap password is checked with `RushDayPasswordValidator.Check` and rejected with the logged codes;
   hashing uses `PasswordHashing.Create()`; `display_name` follows the password source (`Demo Administrator` only when
   the demo password is used; S1 keyed it on `DemoEnabled`); creating the account writes an `account.provisioned`
   audit row with a null actor; the skip notes are `skipped: no password`, `skipped: username taken`, `skipped:
   password rejected by policy`.
7. `demo_accounts`: hashes come from `PasswordHashing.Create()` (210,000 iterations; S1 used the 100,000 default);
   verify-and-reuse the stored hash per role and rewrite a role's hashes set-based when it does not verify as
   `Success`; after the unchanged insert SQL, run the heal `UPDATE` and the `DELETE FROM user_tokens` of `01` step 8.
8. `demo_accounts_disable` (new): the `UPDATE` of `01` step 9, the Warning and the `system.demo_accounts_disabled`
   audit row.
9. `demo_announcements`: the titles and bodies of `01` step 10 (no literal dates; the results notice depends on whether
   the step-3 publication is live) with `ExpiresAt = publish_at + 7 days` for the results notice and the Spring window's
   `closes_at` for the enrolment notice (S1 hard-coded "28 September at 10:00" and "29 January" and set no expiry).
10. `demo_reset_hot_module` (new): the `UPDATE` of `01` step 11 and, when `n > 0`, a `system.demo_reset` audit row with
    a null actor; the SQL is exposed as `public static Task<int> WithdrawDemoHotModuleEnrolmentsAsync(RushDayDbContext db,
    CancellationToken ct)` for S6's `DemoResetService`.
11. `demo_autumn_cohort` (new): the `INSERT ... SELECT` of `01` step 12 with `@currentYear` read from
    `academic_settings`.
12. Audit rows written by backfills are `AuditEvent` entities added to the step's context (`Id = Guid.CreateVersion7()`,
    `OccurredAt = clock.GetUtcNow()`, null actor, `SubjectType = AuditActions.SubjectOf(action)`, `Details` serialised
    with `System.Text.Json` camelCase), committed with the step.

### 5.6 Options, seeding and demo constants

1. `Seeding/StartupBackfillOptions.cs`: add `public string BootstrapAdminUsername { get; init; } = "admin";`
   (everything else already matches `01` section 6).
2. `Seeding/DatabaseSeeder.cs`: new `Enrolment` rows get `AcademicYear = "2025/26"` and new `Grade` rows `Outcome =
   GradeOutcome.Mark` (explicitly).
3. `Seeding/DemoAccounts.cs`: `StudentHint`, `LecturerHint`, `AdminHint` become the static texts of `01` section 7 (S1's
   student hint said "60 autumn credits, results publish 28 Sep 2026 at 10:00 ..."); usernames and passwords unchanged;
   the admin username default moves to `StartupBackfillOptions`.
4. `Seeding/LecturerSeed.cs`: no change (its conditional CS3099 override is exactly `01` step 7's fallback rule, and the
   round-robin already gives CS3099 and CS3001 leader `L00001` and teacher `L00006`).

### 5.7 Connection string builder (`DependencyInjection.cs`)

`BuildConnectionString` stops defaulting `Minimum Pool Size` to 2 (default 0) and stops defaulting `Keepalive` to 30
(no default; `04` section 5); every other default and the override-respecting `SetIfAbsent` behaviour stay.

### 5.8 `src/RushDay.Api/Program.cs` (S1's block only; S2 replaces the file later)

Bind `BootstrapAdminUsername = builder.Configuration["Bootstrap:AdminUsername"] ?? "admin"` into
`StartupBackfillOptions`, and seed only when `seedCommand || (databaseOptions.SeedOnStartup &&
builder.Configuration.GetValue<bool>("Demo:Enabled"))`. `DatabaseOptions.cs` already has `BackfillOnStartup`,
`SeedResultsDay` and `MaxPoolSize` and needs no change.

### 5.9 Unit tests (`tests/RushDay.UnitTests`)

1. `Enrolments/EnrolmentRulesTests`: cases for `ignoreCreditLimit: true` (over the limit → `ModuleFull` or `Accepted`)
   and the unchanged order.
2. `Audit/AuditActionsTests`: the catalogue equals the 48 actions of `03` section 7; every action maps to a member of
   `AuditSubjects.All` through `SubjectOf`; subjects are unique.
3. `Grades/ClassificationTests`: `Graded` excludes `Absent` and `Deferred`; `Band`.
4. `Identity/RushDayPasswordValidatorTests`: `Check` rejects 11 and 129 characters and 3 distinct characters, finds a
   blocklisted entry case-insensitively, and the embedded list loads at least 1,000 entries.
5. `Persistence/ConnectionStringTests` (committed tests plus the uncommitted additions in the working tree): defaults
   are `MinPoolSize = 0` and no keepalive; explicit overrides of either are kept.
6. `Identity/DemoPasswordHashTests` (untracked in the working tree): hash and verify with `PasswordHashing.Create()` and
   assert the V3 header's iteration count is 210,000 (decode the header bytes rather than comparing a base64 prefix;
   its current `V3DefaultPrefix` asserts 100,000).

### 5.10 Already conforming (no change)

Migration file name, timestamp and `[Migration]` attribute; the Identity tables, their snake_case names and the fixed
role ids; `users` columns, the unique `student_id`/`lecturer_id` indexes and `ck_users_one_principal`; the new domain
tables `lecturers`, `module_lecturers` (`ck_module_lecturers_role`), `enrolment_windows` (both CHECKs and the unique
index), `results_publications` (index name `ix_results_publications_semester_created_at`), `announcements` (CHECKs and
indexes), `audit_events` (columns and indexes other than `chain_hash`) and `data_backfills`; `modules.department`
derived and its default dropped, `enrolled_count` and `is_active` defaults kept, `ck_modules_*`; `enrolments.status`
default kept, `ck_enrolments_status`/`ck_enrolments_source`, `ix_enrolments_module_id_status` replacing
`ix_enrolments_module_id`; `grades.published_at` nullable, `status` and `updated_at` defaults dropped, `version` default
kept, `ck_grades_status`, `ck_grades_published_has_instant`, both new grade indexes; every foreign key and delete
behaviour; the extra CHECK names (`ck_announcements_scope`, `ck_results_publications_semester`) and the FK indexes EF
generated, which the spec accepts; `StartupBackfillOptions`' location and `RunAsync(db, options, clock, logger, ct)`;
one transaction per step with its `data_backfills` upsert and the `once` skip; the `roles` SQL; the seed publication
step; the demo-user insert SQL (identical to `01` section 6); `is_demo = true` for an admin created with the demo
password; `DatabaseOptions`; `AddRushDayPersistence(connectionString, maxPoolSize)`; `EnrolmentDecision.WindowClosed`
and the rule order; `EnrolmentWindow.IsOpenAt`/`AllowsWithdrawalAt`; `Module` settable properties and `Level`;
`ResultsPublication`, `Announcement`, `RushDayRoles`, `ApplicationUser`; the unit-test project's reference to
Infrastructure.

### 5.11 Not S1R's work (handled by later stages)

`StartupTasks` with the demo guard, the KEK check and migrations on `ConnectionStrings:Migrations` (S2); the Identity
DI registration using `PasswordHashing.IterationCount` (S2); the factory's fake clock and Production-mode variant (S2);
`MigrationOnSeededDatabaseTests` automating `01` section 9 (S11); the other scripts that call `scripts/run-api.ps1`
(S11: `load.ps1`; S3: `dev.ps1`; S12: `e2e.ps1`).
