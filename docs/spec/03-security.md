# RushDay v1 specification: 03. Security

Scope: threat model, controls, exact response headers and CSP, configuration and secrets per environment, logging
and audit rules, and what is explicitly out of scope. Session, second-factor and rate-limit mechanics are specified
in `02-api.md` sections 2–5 and are referenced, not repeated.

## 1. Assets and adversaries

Assets, in order of harm if compromised: 20,000 students' marks and enrolments (personal data under UK GDPR);
staff and administrator accounts; the Data Protection key ring (with it an attacker mints any session); the enrolment
fairness guarantee (no oversold places, no favoured requests); availability at 09:00 on results day and during
enrolment windows; the audit trail's integrity and completeness (who changed, and who read in bulk).

Adversaries considered: an anonymous attacker on the internet (credential stuffing, password spraying, lockout
denial-of-service against enumerable usernames, DoS); an authenticated student probing other students' data or racing
the enrolment endpoint; a lecturer reaching another module's roster, marks or announcements; a teacher submitting on
the leader's behalf; a hostile web page attempting CSRF or clickjacking against a signed-in user; stored XSS through
any text field; a script hammering login, the readiness probe or enrolment from campus or from one machine; a stolen
container log or database dump; a sibling host on a customer domain planting cookies; a university that applies the
Render Blueprint and would otherwise inherit the demo.

Not considered (section 11): a compromised Render or Neon platform, a compromised administrator, physical access.

## 2. Threat model

| # | Threat | Control | Verified by |
|---|---|---|---|
| T1 | Credential stuffing / password spraying | Per-username window of 10 **failed** logins per minute; per-address 600/min policy; per-address window of 20 **failed** logins per 10 min (both failure windows reserve before the password check and refund on success, so concurrency cannot overrun them; IPv6 keyed by /64); login concurrency guard (8 running, 16 queued, below the global limiter's 24 permits); Identity lockout 5/15 min once an account's failures come from ≥ 3 addresses; constant-time failure path including the locked-out case; generic 401; PBKDF2 at 210,000 iterations; a change-password lockout is audited like a sign-in one | `AuthTests.Lockout_after_five_failures_from_three_addresses` (three `X-Forwarded-For` values; the factory trusts forwarded headers), `AuthTests.Single_address_spray_is_rate_limited_not_locked`, `AuthTests.Unknown_user_wrong_password_and_locked_out_are_indistinguishable`, `LoginProtectionTests` (IPv6 /64, concurrent failures, successes refunded, CPU guard release), `SessionHardeningTests.Change_password_lockout_is_audited_and_stops_further_guesses`; `login-storm.js` (guard and spray modes) |
| T2 | Session theft via XSS | HttpOnly cookie (script cannot read it); CSP `script-src 'self'` with no inline scripts; React escaping; no `dangerouslySetInnerHTML` (ESLint rule plus a repo grep in CI, `scripts/check-story.ps1`) | Header test `SecurityHeadersTests`; CI grep |
| T3 | CSRF against mutating routes | SameSite=Strict cookies; antiforgery header on every non-GET including login and MFA verify; no CORS middleware | `AntiforgeryTests.Post_without_header_is_400` |
| T4 | Horizontal privilege escalation (student → other student) | `/api/me/*` derives identity from claims; no student-role route takes a student number; admin routes require `AdminOnly` | `AuthorizationMatrixTests` (every cross-role call → 403, including an administrator on lecturer routes) |
| T5 | Lecturer reaches another module's roster, marks or announcements; teacher submits | `TeachesModule` policy plus the `module_lecturers` join inside every query; announcements resolved by (`id`, `module_id`, scope); submit requires `Leader` | `AuthorizationMatrixTests.Lecturer_on_foreign_module_is_403_not_your_module`, `..._cannot_edit_announcement_of_another_module`, `..._cannot_delete_university_announcement`, `..Teacher_cannot_submit_marks` |
| T6 | Draft or embargoed marks reach a student, or a withdrawn student sees a mark | Single `GradeQueries.VisibleToStudents(db, now)` filter (Published, `published_at <= now`, enrolment `Active`); only active enrolments' grades are submitted or published; student contracts have no status field; admin view is a different type | `GradeVisibilityTests` with `FakeTimeProvider`, including `Withdrawn_student_never_sees_mark` |
| T7 | Oversold module / lost update | Atomic conditional `UPDATE` on `enrolled_count`; unique index; per-student row lock | `EnrolmentConcurrencyTests` (200 parallel → 30 on a fresh module); `enrolment-rush.js` |
| T8 | Resource exhaustion (DB pool, CPU, connections) | Pool 20 with 5 s bounded wait; global concurrency limiter → fast 503; request timeout 15 s; Kestrel body limit 256 KB, connection limit 2,000, backlog 1,024; PBKDF2 guarded by the login concurrency limiter; the anonymous readiness probe is inside the global limiter and capped at 30/min per address | `dashboard-knee.js` shows 503s not timeouts; `RateLimitTests` incl. `Ready_probe_is_limited` |
| T9 | Clickjacking | `frame-ancestors 'none'`, `X-Frame-Options: DENY` | `SecurityHeadersTests` |
| T10 | Personal data in logs | Logging rules (section 6); EF command logging at Warning in Production; `Include Error Detail=false` | Code review checklist; `LoggingRedactionTests` asserts no name/mark in captured log lines for a dashboard call |
| T11 | Tampered or missing audit trail | Audit row written in the same transaction as the change; a database trigger rejects UPDATE and DELETE on `audit_events` for every role including the application's; bulk reads audited; export | `AuditTests.Every_mutation_writes_a_row`, `AuditTests.Update_and_delete_are_rejected_by_the_database`, `AuditTests.Export_writes_an_audit_row`, `AuditTests.Student_view_writes_an_audit_row` |
| T12 | Account left active after leaving, or locked account keeps a live session | Disable sets `disabled_at` and rotates the security stamp; `RushDaySecurityStampValidator` rejects disabled and locked-out users at every validation interval even when the stamp is unchanged, and the interval runs from the `svt` claim (the last check), never from the cookie's issue time, so a session renewed every minute is still re-checked every 5 minutes; "mark as left" disables the account; logout rotates the stamp | `AuthTests.Disabled_user_session_dies_after_validation_interval`, `AuthTests.Locked_out_user_session_dies_after_validation_interval`, `AuthTests.Cookie_replayed_after_logout_is_rejected` (interval set to 0 in tests), `SessionHardeningTests.Active_session_of_a_disabled_user_ends_within_the_interval` and `SessionHardeningTests.Renewed_cookie_replayed_after_logout_is_rejected_within_the_interval` (the real 5-minute interval, a request every 2 minutes) |
| T13 | Weak or reused temporary passwords | Generated 16-character passwords from `RandomNumberGenerator` over a 57-character alphabet (`A-Z a-z 2-9` minus `I l O`, about 93 bits); shown once; `must_change_password` forces rotation; `Bootstrap:AdminPassword` validated by the same policy | `AccountTests`, `MigrationOnSeededDatabaseTests.Bootstrap_password_rejected_by_policy_is_not_used` |
| T14 | Vulnerable dependencies | `dotnet list package --vulnerable --include-transitive` and `npm audit --omit=dev --audit-level=high` fail CI (a full `npm audit` runs as a non-blocking second step); Dependabot security updates enabled on the repository | CI |
| T15 | Secrets in the repository or image | Only `appsettings.Development.json` holds a local, non-secret connection string; Render env vars for everything else; `.dockerignore` excludes tests, docs, load, scripts; GitHub secret scanning with push protection enabled on the repository (free) | Review; repository settings recorded in `docs/deployment.md` |
| T16 | Demo credentials leaking into a customer deployment | `Demo:Enabled` defaults to false and is absent from `render.yaml`; in every environment but Development it also requires `Demo:PublicDemoAcknowledged`; the seeder runs only in demo mode; every demo account carries `is_demo`, is disabled with a rotated stamp on the first start with demo off, and cannot sign in while demo is off even if a row were re-enabled by hand; a demo session (public password) may change no real account, and what it provisions is itself a demo account; the accounts page shows a banner while any exist | `PublicStatusTests.Demo_null_when_disabled`, `MigrationOnSeededDatabaseTests.Demo_off_disables_every_demo_account`, `AuthTests.Demo_account_cannot_sign_in_when_demo_disabled`, `StartupTests.Production_demo_without_acknowledgement_aborts`, `StartupTests.Staging_demo_without_acknowledgement_aborts`, `DemoActorTests` |
| T17 | Administrator or staff account taken over with a password alone | TOTP second factor mandatory for `Admin` (gated at 403 `mfa-setup-required` until enrolled), voluntary for `Lecturer`, unavailable to `Student`; a code is accepted once (the last accepted time step is stored per user); the password-to-code challenge lives 5 minutes from the password step and dies with any stamp change; the bootstrap username is configurable; the factor can be reset only by an administrator | `AuthTests.Admin_without_mfa_is_gated`, `AuthTests.Mfa_login_round_trip`, `SessionHardeningTests` (replay, challenge expiry and stamp binding, students), `AuthorizationMatrixTests.Admin_gated_by_mfa_cannot_publish` |
| T18 | Stolen database dump used to mint sessions | Data Protection keys encrypted at rest with AES-256-GCM under `DataProtection:KeyEncryptionKey`, which lives only in Render's environment; Production refuses to start without it; at every start outside Development each key stored in plaintext (created before the KEK existed, or by a Development host sharing the database) is revoked through `IKeyManager` (logged as an Error) and an encrypted default key is ensured (`Security/KeyRingHygiene.cs`) | `DataProtectionTests.Key_xml_is_not_plaintext` (Production factory), `DataProtectionTests.Plaintext_keys_are_revoked_when_a_production_host_starts`, `StartupTests.Production_without_kek_aborts` |
| T19 | Lockout denial-of-service against enumerable usernames | Lockout only counts failures from ≥ 3 distinct addresses; a single address is stopped by its own failure window; demo accounts are never locked | `AuthTests.Single_address_spray_is_rate_limited_not_locked`; `login-storm.js` spray mode |
| T20 | Cookie planting from a sibling host on a customer domain; host header abuse | `__Host-` prefixed cookie names outside Development; host filtering from `Security:AllowedHosts` or `RENDER_EXTERNAL_HOSTNAME`; forwarded headers trusted only when `Security:TrustForwardedHeaders` | `SecurityHeadersTests.Production_cookie_names_have_host_prefix`, `HostFilteringTests` |
| T21 | Reconnaissance from the public API description | OpenAPI mapped only in Development; the public index omits the link elsewhere | `IndexEndpointTests.Openapi_absent_in_production` |

Residual risk, stated for the buyer: a **distributed** spray (many addresses, few attempts each) can still lock
accounts it does not own, at the cost of at least 3 addresses per victim (three IPv6 /64s, not three addresses of one)
and 5 attempts each; the per-username window caps it at 10 failed attempts a minute per account and every lockout is
audited with the hashed source addresses. The same attacker can hold one account's per-username window exhausted by
failing 10 times a minute from several addresses, and while it is exhausted the owner's own correct password also
answers 429 (the window is checked before the password, which is what makes it a brake): the owner's successful
sign-ins never spend it, so a single-address attacker is stopped by its own failure window first, but a distributed
one can keep a named account at 429 (or locked) for as long as it keeps failing. A small timing difference also
remains: once an account's failures in the window come from 3 addresses, a failed attempt on an existing account adds
the lockout bookkeeping (`AccessFailedAsync`, a database round trip) that an unknown username does not, so an attacker
already spreading failures over 3 addresses could tell an existing username from an unknown one by latency; the
answers themselves stay identical, and restructuring the failure path to hide the round trip is not worth its cost
here. Mitigating either further needs an IP-reputation or bot-management layer, which is out of scope (section 11).

## 3. Transport and proxy

- Render terminates TLS and forwards `X-Forwarded-For` and `X-Forwarded-Proto`. `UseForwardedHeaders` runs first in the
  pipeline with `ForwardedHeaders = XForwardedFor | XForwardedProto`, `ForwardLimit = 1`; `KnownNetworks` and
  `KnownProxies` are cleared **only when `Security:TrustForwardedHeaders = true`** (`true` in `render.yaml`, in
  `appsettings.Development.json` so k6 and the tests can present synthetic addresses from localhost, and in the test
  factory; `false` by default, so the same image on an unknown host does not let a client choose its own address for
  the per-IP limiters and `ip_hash`). After it, `Request.IsHttps` is true so `Secure` cookies and HSTS behave. Outside
  Development a start with `Security:TrustForwardedHeaders = false` logs a Warning (`StartupTasks.UntrustedForwardedHeadersWarning`):
  behind a TLS-terminating proxy every request then looks like plain HTTP, the `Secure` session and antiforgery cookies
  cannot be issued, and every sign-in and POST fails with 500 (`ProductionHardeningTests.Production_warns_when_forwarded_headers_are_not_trusted`).
- Host filtering (`AllowedHosts`): outside Development the value is `Security:AllowedHosts` when set, **plus**
  `RENDER_EXTERNAL_HOSTNAME` whenever Render sets it (Render injects it, so the Blueprint needs no manual step, and
  Render's health check, which calls that name, keeps passing after a customer domain is configured;
  `HostFilteringTests.Production_keeps_the_render_hostname_alongside_configured_hosts`); with neither, `*` with a
  Warning `Host filtering disabled: set Security__AllowedHosts`. Development keeps `*`.
- No `UseHttpsRedirection`: Render redirects at the edge and the health probe arrives over HTTP.
- HSTS outside Development: `max-age=31536000`, no `includeSubDomains`, no `preload` (we do not own
  `onrender.com`; a customer domain may enable both through `Security:HstsIncludeSubDomains`, `Security:HstsPreload`).
  `SecurityHeadersMiddleware` writes it in its `OnStarting` callback from `HstsOptions`, with `UseHsts()`'s rules
  (HTTPS requests only, not for the loopback hosts `HstsOptions.ExcludedHosts` lists), instead of `UseHsts()` itself:
  the exception handler clears the response headers before it writes a 500 or 503, which drops a header set on the way
  in (`ProductionHardeningTests.Production_error_responses_carry_hsts_and_the_security_headers`).
- Kestrel: `Limits.MaxRequestBodySize = 262144`, `Limits.MaxConcurrentConnections = 2000`,
  `Limits.KeepAliveTimeout = 120 s`, `Limits.RequestHeadersTimeout = 30 s`, `SocketTransportOptions.Backlog = 1024`.
- No CORS middleware exists. Production is same-origin; development uses the Vite proxy.

## 4. Response headers (`Security/SecurityHeadersMiddleware.cs`, every response including static files and errors)

```
Content-Security-Policy: default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; img-src 'self' data:; font-src 'self'; connect-src 'self'; frame-ancestors 'none'; base-uri 'self'; form-action 'self'; object-src 'none'; upgrade-insecure-requests
X-Content-Type-Options: nosniff
X-Frame-Options: DENY
Referrer-Policy: strict-origin-when-cross-origin
Permissions-Policy: camera=(), microphone=(), geolocation=(), payment=(), usb=()
Cross-Origin-Opener-Policy: same-origin
Cross-Origin-Resource-Policy: same-origin
Strict-Transport-Security: max-age=31536000            (outside Development only)
Cache-Control: no-store                                 (every /api response)
```

Exact rules:

- Every response the application pipeline produces carries these headers, the exception handler's 500 and 503 and the
  rate limiters' 429 and 503 included (all written in one `OnStarting` callback). Two kinds of response cannot, and
  this is accepted: host filtering's 400 for a refused `Host` (the middleware runs first of all, before any of ours)
  and Kestrel's own protocol answers (400 malformed request, 413 body over the limit before the app reads it, 431
  headers too large), which the server writes without running the pipeline. None of them carries content a browser
  would render or frame.
- `upgrade-insecure-requests` is omitted in Development (plain `http://localhost`).
- `style-src 'unsafe-inline'` is required because React `style` props, Radix positioning and Recharts write `style`
  attributes; nonces cannot cover attributes. Scripts stay strict: Vite emits `<script type="module" src="/assets/...">`
  and the theme bootstrap is the external file `/theme-init.js`, never inline.
- `img-src data:` allows the SVG favicon, chart exports and the TOTP QR code (a `data:` PNG); no remote images anywhere.
- `font-src 'self'`: Inter is self-hosted from `@fontsource-variable/inter`; no Google Fonts request.
- `/assets/*` (Vite hashed output) gets `Cache-Control: public, max-age=31536000, immutable`; every other static
  file (`index.html`, `theme-init.js`, `favicon.svg`, `data/*.json`) gets `Cache-Control: no-cache` (revalidate, so a
  deploy shows on the next load while the shell stays cacheable). `no-store` is for `/api` responses only.
- Authenticated `/api` responses are never output-cached; only `GET /api/public/status` uses `OutputCache` (10 s, named
  policy `public-status`).

## 5. Input handling

- DataAnnotations on every request record (`[Required]`, `[StringLength]`, `[Range]`, `[RegularExpression]`); .NET 10
  minimal API validation returns 400 `validation` before any handler runs. Every free-text query parameter (`q`,
  `actor`, `action`) carries `[StringLength(100)]` so an `ILIKE` over 80,000 rows never receives an arbitrary-length
  pattern.
- Route constraints for student numbers, module codes, staff numbers and GUIDs (`02-api.md` section 1).
- Query paging clamped; marks batches capped at 500 rows; announcement bodies ≤ 4,000 characters; reasons 10..400.
- JSON `MaxDepth = 16`; bodies over 256 KB rejected by Kestrel (413 `payload-too-large`).
- Every free-text field (`title`, `body`, `description`, `reason`, `displayName`) is stored as-is and rendered as text
  by React; the SPA never sets `innerHTML`; audit `details` are rendered as key/value text.
- Search terms are parameterised through EF Core LINQ (`EF.Functions.ILike` with escaped `%` and `_`); no string
  concatenation into SQL anywhere; raw SQL (`ExecuteSqlInterpolatedAsync`, `NpgsqlCommand` with parameters, backfills)
  uses parameters only.
- CSV exports quote every field and prefix cells starting with `=`, `+`, `-`, `@`, Tab (0x09) or CR (0x0D) with a
  single quote (CSV injection, OWASP set); `Security/CsvInjectionTests`.
- Usernames are restricted to `[A-Za-z0-9._-]` and compared case-insensitively.

## 6. Logging rules

- Production: `AddJsonConsole` **without** scopes (`IncludeScopes = false`): the hosting scope carries the raw request
  path, which would put student numbers, search terms and anything else in a URL on every line
  (`ProductionHardeningTests.Production_json_logs_carry_no_scopes`). The one line per request
  (`RequestLoggingMiddleware`, outside the exception handler so its `statusCode` is the one the client received: 503
  for a transient database failure, 499 for an abandoned request) carries `timestamp`, `level`, `category`, `message`,
  `traceId`, `userId` (GUID), `role`, `route` (the route template, not the raw path), `statusCode`, `elapsedMs`; other
  lines are correlated through it by time and by their own `usernameHash`/`ipHash` fields. Development: plain console.
- Never logged: names, usernames, emails, marks, passwords, cookies, tokens, request or response bodies, raw query
  strings, raw IP addresses. Student numbers and module codes may appear. Failed logins and lockouts log `outcome`,
  `usernameHash` (first 12 hex chars of SHA-256) and `ipHash` (the same keyed daily hash as `audit_events.ip_hash`,
  D32) only, so a spray can be traced in the logs without storing addresses.
- `Microsoft.EntityFrameworkCore.Database.Command` = `Warning` in Production (parameters would leak marks);
  Npgsql `Include Error Detail=false`, forced outside Development whatever the connection string says (only Development
  honours an explicit `Include Error Detail=true`; `ProductionHardeningTests.Include_error_detail_is_honoured_only_in_development`).
- Unhandled exceptions are logged with the stack trace; the client receives 500 `internal-error` with `traceId` only.
- `traceId` in every ProblemDetails maps a user report to its log line.

## 7. Audit rules

- `AuditWriter.Record(db, action, subjectType, subjectId, details, studentId?, moduleId?)` adds an `audit_events`
  entity to the same `DbContext`; the caller's `SaveChangesAsync`/transaction commits it atomically with the change.
  Actor fields come from `CurrentUser` (null for startup steps). `request_id` and `ip_hash` are filled from the request
  (`IpHasher`). `subjectType` is one of `AuditSubjects`; `AuditActions.SubjectOf(action)` is the single mapping and
  `AuditActionsTests` asserts every catalogue action maps to a listed subject.
- The database rejects every UPDATE and DELETE on `audit_events` (trigger `trg_audit_events_immutable`,
  `01-domain-and-data.md` section 3), so the application role, a raw-SQL mistake or an injected statement cannot rewrite
  the trail. The application also has no code path that would try. Retention purge is Could and, when built, runs as an
  admin action that is itself audited and drops and recreates the trigger inside its own transaction.
- Bulk reads of personal data are audited as reads: `audit.exported` is written and committed before the CSV starts
  streaming; `student.viewed` on every `GET /api/admin/students/{n}`; `student.exported` / `student.exported_self` on
  the JSON exports. Login successes and failures are metrics, not audit rows (volume); lockouts are audited.
- `details` holds small JSON only (before/after values, reason, decision flags); never marks of other students, never
  passwords, never raw addresses.
- Should: `chain_hash` per row (`01-domain-and-data.md` section 3) for tamper evidence; `GET /api/admin/audit/verify`
  walks the chain and reports the first break.

Action catalogue (`Domain/Audit/AuditActions.cs`; subject from `AuditSubjects`):

| Action | Subject | Details |
|---|---|---|
| `auth.locked_out` | Account | `{ usernameHash, failedCount, ipHash }` |
| `auth.password_changed` | Account | `{ forced: bool }` |
| `account.mfa_setup_started` / `mfa_enabled` | Account | `{ username }` |
| `enrolment.created` | Enrolment | `{ moduleCode, academicYear, source: 'self', reactivated: bool }` |
| `enrolment.withdrawn` | Enrolment | `{ moduleCode, academicYear }` |
| `enrolment.admin_created` | Enrolment | `{ moduleCode, academicYear, reason, override: true, forceCapacity, capacityRaised }` |
| `enrolment.admin_withdrawn` | Enrolment | `{ moduleCode, academicYear, reason, override: true, left?: true, trim?: true }` |
| `grade.entered` | Grade | `{ moduleCode, studentNumber, mark, outcome }` |
| `grade.changed` | Grade | `{ moduleCode, studentNumber, before: { mark, outcome }, after: { mark, outcome }, version }` |
| `grade.corrected` | Grade | `{ moduleCode, studentNumber, before: { mark, outcome }, after: { mark, outcome }, reason }` |
| `module.marks_submitted` | Module | `{ gradeCount, academicYear }` |
| `module.returned_to_draft` | Module | `{ reason, gradeCount, academicYear, fromScheduledPublication }` |
| `results.published` | Publication | `{ academicYear, semester, publishAt, modules, grades, excluded: [codes], announced }` |
| `results.rescheduled` | Publication | `{ before, after }` |
| `results.cancelled` | Publication | `{ academicYear, semester, grades }` |
| `results.unpublished` | Publication | `{ academicYear, semester, grades, reason }` |
| `announcement.created` / `updated` / `deleted` | Announcement | `{ scope, moduleCode, title }` |
| `account.provisioned` | Account | `{ username, role, studentNumber, staffNumber }` |
| `account.locked` / `unlocked` / `disabled` / `enabled` / `password_reset` / `mfa_reset` | Account | `{ username }` |
| `settings.changed` | Settings | `{ before: {...}, after: {...} }` |
| `window.created` / `updated` / `deleted` | Window | `{ academicYear, semester, before, after }` |
| `module.created` / `updated` | Module | `{ before, after, reason? }` |
| `module.lecturers_set` | Module | `{ before: [staffNumbers], after: [staffNumbers] }` |
| `module.trimmed` | Module | `{ reason, withdrawn: [studentNumbers] }` |
| `student.created` / `updated` / `left` | Student | `{ studentNumber, before?, after?, reason? }` |
| `student.viewed` | Student | `{ studentNumber }` |
| `student.exported` / `student.exported_self` | Student | `{ studentNumber }` |
| `lecturer.created` / `updated` / `left` | Lecturer | `{ staffNumber, before?, after?, reason? }` |
| `audit.exported` | System | `{ filters, rowCount, truncated }` |
| `ops.reconciled` | System | `{ modulesCorrected: [{ code, before, after }] }` |
| `system.demo_reset` | System | `{ moduleCode, withdrawn }` |
| `system.demo_accounts_disabled` | System | `{ count }` |

## 8. Configuration and secrets per environment

| Setting | Development (`appsettings.Development.json`, `launchSettings.json`) | CI / tests | Production (Render env vars) |
|---|---|---|---|
| `ConnectionStrings:RushDay` | `Host=localhost;Port=5432;Database=rushday;Username=rushday;Password=rushday` (not a secret) | Testcontainers string or `RUSHDAY_TEST_CONNECTION` | `ConnectionStrings__RushDay` (`sync: false`, pasted from Neon for the `rushday_app` role; includes `SSL Mode=VerifyFull;Channel Binding=Require`) |
| `ConnectionStrings:Migrations` | unset (same role) | unset | optional `ConnectionStrings__Migrations` (`sync: false`): the Neon owner role, used only by `MigrateAsync`; when unset, migrations run on `ConnectionStrings:RushDay` and `docs/deployment.md` records the residual risk of one role that can `DROP SCHEMA` |
| `Database:MigrateOnStartup` | false (scripts do it) | factory does it | `true` (also the default in every environment but Development) |
| `Database:SeedOnStartup` | false | factory | **not in `render.yaml`**; honoured only when `Demo:Enabled` (D29) |
| `Database:BackfillOnStartup` | false | factory | `true` (also the default in every environment but Development) |
| `Database:StartupCommandTimeoutSeconds` | 600 (default) | 600 (one test sets 321) | 600 (default): the command timeout of every startup statement (migration on either connection, seed, backfills); requests keep the connection string's `Command Timeout=10`, far too short for the grade backfill on Neon's smallest compute, where a timeout would abort every restart |
| `Database:SeedStudentCount` | 20000 | 300 | **not in `render.yaml`**; `20000` set in the dashboard for the public demo |
| `Database:SeedResultsDay` | `2026-09-28T09:00:00Z` | `2026-01-26T09:00:00Z` | `2026-09-28T09:00:00Z` (default) |
| `Database:MaxPoolSize` | 40 | 20 | `20` (default) |
| `Demo:Enabled` | true | true | **not in `render.yaml`**; `Demo__Enabled=true` set in the Render dashboard for the public demo only; unset (false) for a customer |
| `Demo:PublicDemoAcknowledged` | n/a | n/a | `Demo__PublicDemoAcknowledged=true` in the dashboard for the public demo; without it a Production start with demo on aborts; with it every start logs `DEMO MODE: every account has a published password` |
| `Bootstrap:AdminUsername` | `admin` | `admin` | optional; default `admin`; a customer who evaluated with demo mode first sets another name because `admin` is then a disabled demo account |
| `Bootstrap:AdminPassword` | unset | unset | required when demo is off; validated by the password policy; consumed only when no usable `Admin` exists; removed from Render after first use |
| `Branding:InstitutionName` / `InstitutionShortName` / `TimeZone` | `RushDay Demo University` / `RushDay` / `Europe/London` | same | env vars, used only to create the settings row |
| `Branding:PrivacyNoticeUrl` | unset | unset | optional URL rendered in the login footer and the user menu (a customer's privacy notice) |
| `Branding:ResultsFootnote` | default `Below 40? Your personal tutor or the academic office can explain resit options.` | same | optional override |
| `DataProtection:KeyEncryptionKey` | unset (plain keys) | set to a fixed test value by the Production-mode factory | `DataProtection__KeyEncryptionKey` (`sync: false`): 32 random bytes, base64 (`[Convert]::ToBase64String((1..32 \| ForEach-Object { Get-Random -Maximum 256 }))` is **not** acceptable; use `openssl rand -base64 32` or `[Convert]::ToBase64String([System.Security.Cryptography.RandomNumberGenerator]::GetBytes(32))`); required; losing it invalidates every session and antiforgery token and nothing else |
| `RateLimiting:*` | `MaxConcurrent=64, MaxQueued=1024, LoginPerIpPerMinute=100000, LoginPerUserPerMinute=100000, LoginFailuresPerIpPer10Minutes=100000, LockoutDistinctIps=3, LoginConcurrency=64, LoginQueue=512, PasswordChangePerUserPerMinute=1000, EnrolPerUserPer10s=1000, WritePerUserPerMinute=10000, HealthReadyPerIpPerMinute=100000, OpsMetricsPer2s=1` | as Development except `LoginPerUserPerMinute=2` in one rate-limit test | defaults from `02-api.md` section 5 (`MaxConcurrent=24, MaxQueued=96, LoginPerIpPerMinute=600, LoginPerUserPerMinute=10, LoginFailuresPerIpPer10Minutes=20, LockoutDistinctIps=3, LoginConcurrency=8, LoginQueue=16, PasswordChangePerUserPerMinute=5, EnrolPerUserPer10s=5, WritePerUserPerMinute=120, HealthReadyPerIpPerMinute=30, OpsMetricsPer2s=1`) |
| `Auth:SessionSlidingHours` / `SessionAbsoluteHours` | 8 / 12 | 8 / 12 | 8 / 12 |
| `Auth:AdminSessionSlidingMinutes` / `AdminSessionAbsoluteHours` | 60 / 8 | 60 / 8 | 60 / 8 |
| `Auth:SecurityStampIntervalMinutes` | 5 | 0 (tests, Playwright) | 5 |
| `Auth:RequireMfaForRoles` | `["Admin"]` | `["Admin"]` | `["Admin"]` (a customer may add `Lecturer`) |
| `Auth:MfaCookieMinutes` | 5 | 5 | 5 |
| `Security:TrustForwardedHeaders` | true (k6 and tests present synthetic addresses) | true | `true` in `render.yaml` (Render's proxy is the only peer); false by default elsewhere |
| `Security:AllowedHosts` | unset (`*`) | unset | optional; else `RENDER_EXTERNAL_HOSTNAME` |
| `Security:HstsIncludeSubDomains` / `HstsPreload` | n/a | n/a | false / false |
| `RENDER_GIT_COMMIT`, `RENDER_EXTERNAL_HOSTNAME` | absent → `local` | absent | set by Render |
| `ASPNETCORE_ENVIRONMENT`, `ASPNETCORE_HTTP_PORTS`, `PORT` | Development, 5080 | Development (Production for the header, KEK and cookie-name tests) | Production, 8080, 8080 |

Rules: no secret in git, in Docker layers or in logs; `render.yaml` is the customer deployment path, so it contains
**no** `Demo__*` or `Database__Seed*` values and marks `ConnectionStrings__RushDay`, `ConnectionStrings__Migrations`,
`DataProtection__KeyEncryptionKey` and `Bootstrap__AdminPassword` as `sync: false` (Render prompts at Blueprint apply);
the public demo's values live only in the Render dashboard; `Bootstrap:AdminPassword` is removed from Render after
first use. Data Protection keys live in `data_protection_keys` encrypted under the KEK, so the database backup covers
session continuity as long as the KEK is kept.

Database role: the application connects as `rushday_app` (`CREATE ROLE rushday_app LOGIN PASSWORD '…'; GRANT CONNECT
ON DATABASE neondb TO rushday_app; GRANT USAGE ON SCHEMA public TO rushday_app; GRANT SELECT, INSERT, UPDATE, DELETE ON
ALL TABLES IN SCHEMA public TO rushday_app; GRANT USAGE, SELECT ON ALL SEQUENCES IN SCHEMA public TO rushday_app; ALTER
DEFAULT PRIVILEGES FOR ROLE neondb_owner IN SCHEMA public GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO
rushday_app; ALTER DEFAULT PRIVILEGES FOR ROLE neondb_owner IN SCHEMA public GRANT USAGE, SELECT ON SEQUENCES TO
rushday_app;`) and the owner role only through `ConnectionStrings:Migrations`. `TRUNCATE`, DDL and trigger control
are deliberately not granted: row triggers do not fire on `TRUNCATE`, so withholding it is what makes the
`audit_events` trigger binding on the application role. The migrations connection, the backfills and the running app
must agree: `MigrateAsync` uses `ConnectionStrings:Migrations` when set, and the backfills and every request use
`ConnectionStrings:RushDay` (the backfills need DML only). `docs/deployment.md` (S11) carries this
script, the "Rotate the database password" procedure (Neon reset → Render env var → redeploy → confirm
`/api/health/ready`), the KEK generation command, the "Deploying for a customer" section (leave unset:
`Demo__Enabled`, `Demo__PublicDemoAcknowledged`, `Database__SeedOnStartup`, `Database__SeedStudentCount`; set:
`Bootstrap__AdminPassword`, `Bootstrap__AdminUsername` if `admin` was a demo account, `DataProtection__KeyEncryptionKey`,
`ConnectionStrings__RushDay` for `rushday_app`, optionally `ConnectionStrings__Migrations`, `Branding__*`,
`Security__AllowedHosts` for a custom domain) and a credential rotation log (Neon password rotated on <date>, KEK
created on <date>). The Neon password that was handled in chat during the first deploy is rotated before S13
(`06-implementation-plan.md` stage S0, an owner action).

## 9. Container and dependency hygiene

- Dockerfile runtime stage is `mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled` (non-root `app` user, no shell,
  no package manager; time-zone data is not needed because the server never formats in a zone, D26), listens on 8080.
  Base images are pinned by tag plus digest (`@sha256:…`, refreshed by Dependabot's `docker` ecosystem).
- `.dockerignore` excludes `.git`, `.github`, `docs`, `load`, `tests`, `scripts`, `**/node_modules`, `**/playwright-report`,
  `**/test-results`, `**/coverage`, `src/RushDay.Web/e2e`.
- CI: `dotnet list package --vulnerable --include-transitive` (fails on any finding), `npm audit --omit=dev
  --audit-level=high` as the gate and a full `npm audit` as a second step with `continue-on-error: true`, `npm ci` from
  the lock file only. GitHub Actions are pinned by commit SHA (with the version in a comment) and kept current by
  Dependabot (`github-actions`, `docker`, `nuget`, `npm` ecosystems, weekly; security updates enabled).
- Branch protection on `main`: required checks `web`, `build-and-test`, `e2e`; no force pushes; the setting and the
  secret-scanning/push-protection setting are recorded in `docs/deployment.md`.
- Warnings are errors in every .NET project; `--max-warnings 0` for ESLint.

## 10. Personal data (UK GDPR) notes for the buyer

- Data held per student: number, name, programme, year, optional email, enrolments, marks, login metadata
  (`last_login_at`, lockout state), audit rows that reference them. No IP addresses are stored: `audit_events.ip_hash`
  and the log lines hold `hex(HMAC-SHA256(dailyKey, ip))[..32]` where the daily key is derived from the Data
  Protection key-encryption key and the UTC date and is never stored, so the value is pseudonymised and cannot be
  reversed or brute-forced without the key ring (D32).
- Where data lives: the public demo runs on Render (Frankfurt) with Neon (AWS eu-central-1, Frankfurt); processing is
  in the EU under the UK adequacy regulations. A customer deployment chooses its own Render region and Neon region and
  records them in `docs/deployment.md`.
- Demo data is synthetic: the login page states "Demo data: 20,000 synthetic students, no real people."
- Privacy notice: `Branding:PrivacyNoticeUrl` is rendered in the login footer and the user menu when set. The login
  footer states exactly what the browser stores: "Only your session cookie and your theme choice are stored. No
  third-party scripts."
- Export of a student's data: `GET /api/me/export.json` (the student) and `GET /api/admin/students/{n}/export.json` (the
  academic office, for a subject-access request); both audited. `GET /api/admin/audit/export.csv` answers "who viewed or
  exported this student's data" because reads are audited (section 7).
- Retention: audit rows are kept indefinitely in v1; a purge is Could and documented as such in `docs/admin-guide.md`.
- Backup and restore: Neon point-in-time restore documented and rehearsed once (`docs/deployment.md`); the restore
  window stated there is the value **measured on the free plan at the time of the rehearsal** (S13 checks the current
  figure in the Neon console rather than repeating a number from memory).

## 11. Explicitly out of scope for v1

- Single sign-on (Entra ID / SAML) and self-service password reset (no email channel on the free tier; the Identity
  external-login extension point is documented for SSO). The TOTP second factor **is** in scope (D27).
- Alerting and paging (no destination on the free tier; the ops page updates while it is open and says so).
- Web application firewall, bot management, IP reputation (the residual distributed-spray risk of section 2).
- Field-level encryption of marks at rest (Neon encrypts storage; application-level encryption would defeat set-based
  queries with no measured threat to justify it).
- Penetration test and formal accessibility audit by a third party (the checklist and automated scans are the v1 evidence).
- Secrets scanning of git **history** and SBOM generation (push protection for new commits is on).
- Protection against a compromised administrator account beyond the second factor and the audit trail.
