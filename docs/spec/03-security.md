# RushDay v1 specification: 03. Security

Scope: threat model, controls, exact response headers and CSP, configuration and secrets per environment, logging
and audit rules, and what is explicitly out of scope. Session mechanics are specified in `02-api.md` sections 2–5
and are referenced, not repeated.

## 1. Assets and adversaries

Assets, in order of harm if compromised: 20,000 students' marks and enrolments (personal data under UK GDPR);
staff and administrator accounts; the enrolment fairness guarantee (no oversold places, no favoured requests);
availability at 09:00 on results day and during enrolment windows; the audit trail's integrity.

Adversaries considered: an anonymous attacker on the internet (credential stuffing, DoS); an authenticated student
probing other students' data or racing the enrolment endpoint; a lecturer reaching another module's roster or marks; a
hostile web page attempting CSRF or clickjacking against a signed-in user; stored XSS through any text field; a
script hammering login or enrolment from campus or from one machine; a stolen container log or database dump.

Not considered (section 9): a compromised Render or Neon platform, a compromised administrator, physical access.

## 2. Threat model

| # | Threat | Control | Verified by |
|---|---|---|---|
| T1 | Credential stuffing / password spraying | Identity lockout 5/15 min; per-username 10/min window; per-IP 600/min backstop; login concurrency guard; constant-time failure path; generic 401 | Integration tests `AuthTests.Lockout_after_five_failures`, `AuthTests.Unknown_user_and_wrong_password_are_indistinguishable`; `login-storm.js` |
| T2 | Session theft via XSS | HttpOnly cookie (script cannot read it); CSP `script-src 'self'` with no inline scripts; React escaping; no `dangerouslySetInnerHTML` (ESLint `react/no-danger` equivalent rule: a repo grep in CI, `scripts/check-story.ps1` also greps for it) | Header test `SecurityHeadersTests`; CI grep |
| T3 | CSRF against mutating routes | SameSite=Strict cookies; antiforgery header on every non-GET including login; no CORS middleware | `AntiforgeryTests.Post_without_header_is_400` |
| T4 | Horizontal privilege escalation (student → other student) | `/api/me/*` derives identity from claims; no student-role route takes a student number; admin routes require `AdminOnly` | `AuthorizationMatrixTests` (every cross-role call → 403) |
| T5 | Lecturer reaches another module | `TeachesModule` policy plus `module_lecturers` join inside the query | `AuthorizationMatrixTests.Lecturer_on_foreign_module_is_403_not_your_module` |
| T6 | Draft or embargoed marks reach a student | Single `GradeQueries.VisibleToStudents(db, now)` filter; student contracts have no status field; admin view is a different type | `GradeVisibilityTests` with `FakeTimeProvider` |
| T7 | Oversold module / lost update | Atomic conditional `UPDATE` on `enrolled_count`; unique index; per-student row lock | `EnrolmentConcurrencyTests` (200 parallel → 30); `enrolment-rush.js` |
| T8 | Resource exhaustion (DB pool, CPU, connections) | Pool 20 with 5 s bounded wait; global concurrency limiter → fast 503; request timeout 15 s; Kestrel body limit 256 KB, connection limit 2,000, backlog 1,024; PBKDF2 guarded by the login concurrency limiter | `dashboard-knee.js` shows 503s not timeouts; `RateLimitTests` |
| T9 | Clickjacking | `frame-ancestors 'none'`, `X-Frame-Options: DENY` | `SecurityHeadersTests` |
| T10 | Personal data in logs | Logging rules (section 6); EF command logging at Warning in Production; `Include Error Detail=false` | Code review checklist; `LoggingRedactionTests` asserts no name/mark in captured log lines for a dashboard call |
| T11 | Tampered or missing audit trail | Audit row written in the same transaction as the change; no update/delete path; export | `AuditTests.Every_mutation_writes_a_row` |
| T12 | Account left active after leaving | Disable sets `disabled_at` and rotates the security stamp; sessions die within 5 minutes | `AuthTests.Disabled_user_session_dies_after_validation_interval` (interval set to 0 in tests) |
| T13 | Weak or reused temporary passwords | Generated 16-character passwords from `RandomNumberGenerator`; shown once; `must_change_password` forces rotation | `AccountTests` |
| T14 | Vulnerable dependencies | `dotnet list package --vulnerable --include-transitive` and `npm audit --audit-level=high` fail CI | CI |
| T15 | Secrets in the repository or image | Only `appsettings.Development.json` holds a local, non-secret connection string; Render env vars for everything else; `.dockerignore` excludes tests, docs, load, scripts | Review; `gitleaks`-style grep is out of scope |
| T16 | Demo credentials leaking into a customer deployment | `Demo:Enabled` defaults to false; demo accounts carry `is_demo` and the accounts page shows a banner while any exist | `PublicStatusTests.Demo_null_when_disabled` |

## 3. Transport and proxy

- Render terminates TLS and forwards `X-Forwarded-For` and `X-Forwarded-Proto`. `UseForwardedHeaders` runs first in the
  pipeline with `ForwardedHeaders = XForwardedFor | XForwardedProto`, `ForwardLimit = 1`, `KnownNetworks.Clear()`,
  `KnownProxies.Clear()` (Render's proxy addresses are not published and only the proxy can reach the container).
  After it, `Request.IsHttps` is true so `Secure` cookies and HSTS behave.
- No `UseHttpsRedirection`: Render redirects at the edge and the health probe arrives over HTTP.
- `UseHsts()` outside Development: `max-age=31536000`, no `includeSubDomains`, no `preload` (we do not own
  `onrender.com`; a customer domain may enable both through `Security:HstsIncludeSubDomains`, `Security:HstsPreload`).
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
Cache-Control: no-store                                 (every /api response and index.html)
```

Exact rules:

- `upgrade-insecure-requests` is omitted in Development (plain `http://localhost`).
- `style-src 'unsafe-inline'` is required because React `style` props, Radix positioning and Recharts write `style`
  attributes; nonces cannot cover attributes. Scripts stay strict: Vite emits `<script type="module" src="/assets/...">`
  and the theme bootstrap is the external file `/theme-init.js`, never inline.
- `img-src data:` allows the SVG favicon and chart exports; no remote images anywhere.
- `font-src 'self'`: Inter is self-hosted from `@fontsource-variable/inter`; no Google Fonts request.
- `/assets/*` (Vite hashed output) gets `Cache-Control: public, max-age=31536000, immutable`; every other static
  file (`index.html`, `theme-init.js`, `favicon.svg`, `data/*.json`) gets `Cache-Control: no-cache`.
- Authenticated `/api` responses are never output-cached; only `GET /api/public/status` uses `OutputCache` (10 s).

## 5. Input handling

- DataAnnotations on every request record (`[Required]`, `[StringLength]`, `[Range]`, `[RegularExpression]`); .NET 10
  minimal API validation returns 400 `validation` before any handler runs.
- Route constraints for student numbers, module codes, staff numbers and GUIDs (`02-api.md` section 1).
- Query paging clamped; marks batches capped at 500 rows; announcement bodies ≤ 4,000 characters; reasons 10..400.
- JSON `MaxDepth = 16`; bodies over 256 KB rejected by Kestrel (413 `payload-too-large`).
- Every free-text field (`title`, `body`, `description`, `reason`, `displayName`) is stored as-is and rendered as text
  by React; the SPA never sets `innerHTML`; audit `details` are rendered as key/value text.
- Search terms are parameterised through EF Core LINQ (`EF.Functions.ILike` with escaped `%` and `_`); no string
  concatenation into SQL anywhere; raw SQL (`ExecuteSqlInterpolatedAsync`, backfills) uses parameters only.
- CSV exports quote every field and prefix cells starting with `=`, `+`, `-`, `@` with a single quote (CSV injection).
- Usernames are restricted to `[A-Za-z0-9._-]` and compared case-insensitively.

## 6. Logging rules

- Production: `AddJsonConsole` with scopes; each line carries `timestamp`, `level`, `category`, `message`, `traceId`,
  `userId` (GUID), `role`, `route` (the route template, not the raw path), `statusCode`, `elapsedMs`. Development: plain
  console.
- Never logged: names, usernames, emails, marks, passwords, cookies, tokens, request or response bodies, raw query
  strings. Student numbers and module codes may appear. Failed logins log `outcome` and `usernameHash` (first 12 hex
  chars of SHA-256) only.
- `Microsoft.EntityFrameworkCore.Database.Command` = `Warning` in Production (parameters would leak marks);
  Npgsql `Include Error Detail=false`.
- Unhandled exceptions are logged with the stack trace; the client receives 500 `internal-error` with `traceId` only.
- `traceId` in every ProblemDetails maps a user report to its log line.

## 7. Audit rules

- `AuditWriter.Record(db, action, subjectType, subjectId, details, studentId?, moduleId?)` adds an `audit_events`
  entity to the same `DbContext`; the caller's `SaveChangesAsync`/transaction commits it atomically with the change.
  Actor fields come from `CurrentUser` (null for startup steps). `request_id` and `ip_hash` are filled from the request.
- The application has no code path that updates or deletes audit rows. Retention purge is Could and, when built,
  runs as an admin action that is itself audited.
- Login successes and failures are metrics, not audit rows (volume); lockouts are audited.
- `details` holds small JSON only (before/after values, reason, decision flags); never marks of other students, never
  passwords.

Action catalogue (`Domain/Audit/AuditActions.cs`):

| Action | Subject | Details |
|---|---|---|
| `auth.locked_out` | Account | `{ usernameHash, failedCount }` |
| `auth.password_changed` | Account | `{ forced: bool }` |
| `enrolment.created` | Enrolment | `{ moduleCode, source: 'self', reactivated: bool }` |
| `enrolment.withdrawn` | Enrolment | `{ moduleCode }` |
| `enrolment.admin_created` | Enrolment | `{ moduleCode, reason, override: true, forceCapacity }` |
| `enrolment.admin_withdrawn` | Enrolment | `{ moduleCode, reason, override: true }` |
| `grade.entered` | Grade | `{ moduleCode, studentNumber, mark }` |
| `grade.changed` | Grade | `{ moduleCode, studentNumber, before, after, version }` |
| `module.marks_submitted` | Module | `{ gradeCount }` |
| `module.returned_to_draft` | Module | `{ reason, gradeCount }` |
| `results.published` | Publication | `{ academicYear, semester, publishAt, modules, grades, excluded: [codes] }` |
| `results.rescheduled` | Publication | `{ before, after }` |
| `announcement.created` / `updated` / `deleted` | Announcement | `{ scope, moduleCode, title }` |
| `account.provisioned` | Account | `{ username, role, studentNumber, staffNumber }` |
| `account.locked` / `unlocked` / `disabled` / `enabled` / `password_reset` | Account | `{ username }` |
| `settings.changed` | Settings | `{ before: {...}, after: {...} }` |
| `window.created` / `updated` / `deleted` | Window | `{ academicYear, semester, before, after }` |
| `module.created` / `updated` | Module | `{ before, after, reason? }` |
| `module.lecturers_set` | Module | `{ before: [staffNumbers], after: [staffNumbers] }` |
| `student.created`, `lecturer.created` | Student / Lecturer | `{ studentNumber \| staffNumber }` |
| `ops.reconciled` | System | `{ modulesCorrected: [{ code, before, after }] }` |

## 8. Configuration and secrets per environment

| Setting | Development (`appsettings.Development.json`, `launchSettings.json`) | CI | Production (Render env vars) |
|---|---|---|---|
| `ConnectionStrings:RushDay` | `Host=localhost;Port=5432;Database=rushday;Username=rushday;Password=rushday` (not a secret) | Testcontainers string or `RUSHDAY_TEST_CONNECTION` | `ConnectionStrings__RushDay` (`sync: false`, pasted from Neon; includes `SSL Mode=Require;Channel Binding=Require`) |
| `Database:MigrateOnStartup` | false (scripts do it) | factory does it | `true` |
| `Database:SeedOnStartup` | false | factory | `true` |
| `Database:BackfillOnStartup` | false | factory | `true` |
| `Database:SeedStudentCount` | 20000 | 300 | `20000` |
| `Database:SeedResultsDay` | `2026-09-28T09:00:00Z` | `2026-01-26T09:00:00Z` | `2026-09-28T09:00:00Z` (default) |
| `Database:MaxPoolSize` | 40 | 20 | `20` (default) |
| `Demo:Enabled` | true | true | `Demo__Enabled=true` on the public demo; **unset (false) for a customer** |
| `Bootstrap:AdminPassword` | unset | unset | optional; required when demo is off; consumed only when no `Admin` exists |
| `Branding:InstitutionName` / `InstitutionShortName` / `TimeZone` | `RushDay Demo University` / `RushDay` / `Europe/London` | same | env vars, used only to create the settings row |
| `RateLimiting:*` | `MaxConcurrent=64, MaxQueued=256, LoginPerUserPerMinute=100000, LoginPerIpPerMinute=100000, LoginConcurrency=64, LoginQueue=512, EnrolPerUserPer10s=1000, WritePerUserPerMinute=10000` | as Development | defaults from `02-api.md` section 5 |
| `Auth:SessionSlidingHours` / `SessionAbsoluteHours` / `SecurityStampIntervalMinutes` | 8 / 12 / 5 | 8 / 12 / 0 (tests) | 8 / 12 / 5 |
| `Security:HstsIncludeSubDomains` / `HstsPreload` | n/a | n/a | false / false |
| `RENDER_GIT_COMMIT` | absent → `local` | absent | set by Render |
| `ASPNETCORE_ENVIRONMENT`, `ASPNETCORE_HTTP_PORTS`, `PORT` | Development, 5080 | Development | Production, 8080, 8080 |

Rules: no secret in git, in Docker layers or in logs; `render.yaml` marks the connection string `sync: false`;
`Bootstrap:AdminPassword` is removed from Render after first use (documented in `docs/deployment.md`); Data Protection
keys live in `data_protection_keys`, so the database backup covers session continuity.

## 9. Container and dependency hygiene

- Dockerfile runtime stage runs as the non-root `app` user provided by `mcr.microsoft.com/dotnet/aspnet:10.0`
  (`USER app`), listens on 8080, no shell tools added.
- `.dockerignore` excludes `.git`, `.github`, `docs`, `load`, `tests`, `scripts`, `**/node_modules`, `**/playwright-report`,
  `**/test-results`, `**/coverage`, `src/RushDay.Web/e2e`.
- CI: `dotnet list package --vulnerable --include-transitive` (fails on any finding), `npm audit --audit-level=high`,
  `npm ci` from the lock file only. Dependabot or Renovate is a recommended follow-up, not a v1 deliverable.
- Warnings are errors in every .NET project; `--max-warnings 0` for ESLint.

## 10. Personal data (UK GDPR) notes for the buyer

- Data held per student: number, name, programme, year, optional email, enrolments, marks, login metadata
  (`last_login_at`, lockout state), audit rows that reference them. No IP addresses are stored in clear (hashed with a
  daily salt in `audit_events.ip_hash`).
- Demo data is synthetic: the login page states "Demo data: 20,000 synthetic students, no real people."
- Export of a student's data (Should): `GET /api/me/export.json` returning the `AdminStudentView`-shaped record minus
  audit details of other actors.
- Retention: audit rows are kept indefinitely in v1; a purge is Could and documented as such in `docs/admin-guide.md`.
- Backup and restore: Neon point-in-time restore (7 days on the free tier) documented and rehearsed once
  (`docs/deployment.md`).

## 11. Explicitly out of scope for v1

- Multi-factor authentication, single sign-on (Entra ID / SAML) and self-service password reset (no email channel on
  the free tier; the Identity external-login extension point is documented for SSO).
- Alerting and paging (no destination on the free tier; the ops page is pull-only).
- Web application firewall, bot management, IP reputation.
- Field-level encryption of marks at rest (Neon encrypts storage; application-level encryption would defeat set-based
  queries with no measured threat to justify it).
- Penetration test and formal accessibility audit by a third party (the checklist and automated scans are the v1 evidence).
- Secrets scanning of git history and SBOM generation.
- Protection against a compromised administrator account beyond the audit trail.
