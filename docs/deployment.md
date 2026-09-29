# Deployment

Live demo: **https://rushday-api.onrender.com** — Render free web service (Docker, Frankfurt), backed by Neon free
PostgreSQL (AWS `eu-central-1`, Frankfurt). Deploys are gated on GitHub Actions: `render.yaml` sets
`autoDeployTrigger: checksPass`, so Render only deploys a commit once its required checks (`web`, `build-and-test`,
and from stage S12 onward `e2e`) have passed — a red build on `main` never reaches the live URL (ADR 6).

This document is the deployment reference for both the public demo and a customer's own institution. Section 1-3
walk a first-time setup; section 4 is the complete configuration table; section 5 is what changes for a customer who
wants their own data, not RushDay's demo; sections 6-9 are the operational procedures (password rotation, the
encryption key, backup and restore, repository hygiene).

## 1. Neon (database)

1. Sign up at https://neon.tech (GitHub login works, no card).
2. Create a project, region **Europe (Frankfurt, `eu-central-1`)**, PostgreSQL 18.
3. Open **Connect**, choose **.NET** as the connection type, copy the connection string. It looks like:
   `Host=ep-xxxx.eu-central-1.aws.neon.tech;Database=neondb;Username=neondb_owner;Password=...;SSL Mode=Require;Channel Binding=Require`
   — this is the **owner** role's string, used only for migrations (section 4) and for creating the application role
   (section 6). The running application never connects as the owner.
4. Create the application role and grant it exactly what it needs (section 6) before the first deploy that will
   carry real data; the public demo and early development can run on the owner role temporarily, but a customer
   deployment should not.

## 2. Render (API)

1. Sign up at https://render.com (GitHub login, no card).
2. **New > Blueprint**, pick the `RushDay` repository. Render reads `render.yaml`.
3. Render prompts for every `sync: false` value in the Blueprint (section 4 lists them): paste the `rushday_app`
   connection string for `ConnectionStrings__RushDay`, optionally the owner string for `ConnectionStrings__Migrations`,
   a freshly generated key for `DataProtection__KeyEncryptionKey` (section 7), and the institution's own values for
   `Bootstrap__AdminPassword` and `Branding__*`. Apply.
4. The first deploy runs the migration and the idempotent startup backfills (`01-domain-and-data.md` section 6); for
   a customer database that means roles, academic settings and the bootstrap administrator, and nothing synthetic —
   `render.yaml` sets no `Demo__*` or `Database__Seed*` key (D29). Watch the service log for `Database migrated`,
   `Startup backfills complete`, and confirm no `DEMO MODE` warning appears (that warning is expected, and correct,
   only on the public demo — section 5).
5. The service's URL is `https://<service-name>.onrender.com` (or a custom domain, set `Security__AllowedHosts`
   accordingly). Try `/`, `/api/health/live`, `/login`.
6. For the **public demo specifically** (this repository's own deployment, not a customer's), also set in the Render
   dashboard (never in `render.yaml`): `Demo__Enabled=true`, `Demo__PublicDemoAcknowledged=true`. Without the second
   variable a Production start with demo mode on refuses to start at all (`StartupTasks`,
   `Demo mode on a Production deployment requires Demo__PublicDemoAcknowledged=true`) — this is deliberate: an
   operator cannot accidentally ship 20,041 published demo passwords by forgetting a checkbox.

## 3. CI/CD

`.github/workflows/ci.yml` runs on every pull request against `v1` and on pushes to `main`: `web` (front-end lint,
typecheck, unit tests, build, bundle budget), `build-and-test` (.NET unit and integration tests against
Testcontainers, the Docker image build, `dotnet list package --vulnerable`, `npm audit`, the story check), and, from
stage S12, `e2e` (Playwright journeys and accessibility). `main` is protected: all three are required checks, so
Render's `checksPass` trigger and GitHub's own merge protection enforce the same bar (section 9). No deploy-hook
secret is needed — `checksPass` is Render's own gate on the commit's check-run status.

## 4. Configuration reference

Every variable RushDay reads, and what each environment sets it to (`docs/spec/03-security.md` section 8 is the
source of truth; this table restates it as a deployment checklist).

| Setting | Development | CI / tests | Production (Render) |
|---|---|---|---|
| `ConnectionStrings:RushDay` | `Host=localhost;Port=5432;Database=rushday;Username=rushday;Password=rushday` (not a secret) | Testcontainers string, or `RUSHDAY_TEST_CONNECTION` | `ConnectionStrings__RushDay` (`sync: false`) — the `rushday_app` role's string, `SSL Mode=VerifyFull;Channel Binding=Require` |
| `ConnectionStrings:Migrations` | unset | unset | optional `ConnectionStrings__Migrations` (`sync: false`) — the Neon owner role, used only by the migration step; unset means migrations run on the application role and `docs/deployment.md` (here) records that residual risk |
| `Database:MigrateOnStartup` | false (scripts do it) | true (factory) | `true` |
| `Database:BackfillOnStartup` | false | true (factory) | `true` |
| `Database:SeedOnStartup` | false | true (factory) | **not set** in `render.yaml`; only `Demo__Enabled=true` in the dashboard turns it on, and only when this key is also present there |
| `Database:SeedStudentCount` | 20000 | 300 | **not set** in `render.yaml`; `20000` set in the dashboard for the public demo only |
| `Database:SeedResultsDay` | `2026-09-28T09:00:00Z` | `2026-01-26T09:00:00Z` | default `2026-09-28T09:00:00Z` |
| `Database:MaxPoolSize` | 40 | 20 | 20 |
| `Database:StartupCommandTimeoutSeconds` | 600 (default) | 600 | 600 |
| `Demo:Enabled` | true | true | **not set** in `render.yaml`; `Demo__Enabled=true` in the dashboard, public demo only — leave unset for a customer |
| `Demo:PublicDemoAcknowledged` | n/a | n/a | `Demo__PublicDemoAcknowledged=true` in the dashboard, public demo only; required whenever `Demo:Enabled=true` in Production, else startup aborts |
| `Bootstrap:AdminUsername` | `admin` | `admin` | optional, default `admin`; set to something else if a customer evaluated with demo mode first (`admin` is then a disabled demo account) |
| `Bootstrap:AdminPassword` | unset | unset | required when demo is off; validated by the password policy; consumed once (the first start with no usable administrator) and should be removed from Render after that first successful login |
| `Branding:InstitutionName` / `InstitutionShortName` / `TimeZone` | `RushDay Demo University` / `RushDay` / `Europe/London` | same | set from the customer's own values |
| `Branding:PrivacyNoticeUrl` | unset | unset | optional: the customer's own privacy notice URL, rendered in the login footer and user menu |
| `Branding:ResultsFootnote` | default (resit wording) | same | optional override |
| `DataProtection:KeyEncryptionKey` | unset (plain keys, Development only) | fixed test value | `DataProtection__KeyEncryptionKey` (`sync: false`) — required; section 7 |
| `RateLimiting:*` | relaxed (section 8 of the spec has every value) | as Development, one test tightens one value | production defaults (`docs/spec/02-api.md` section 5) |
| `Auth:SessionSlidingHours` / `SessionAbsoluteHours` | 8 / 12 | 8 / 12 | 8 / 12 |
| `Auth:AdminSessionSlidingMinutes` / `AdminSessionAbsoluteHours` | 60 / 8 | 60 / 8 | 60 / 8 |
| `Auth:SecurityStampIntervalMinutes` | 5 | 0 | 5 |
| `Auth:RequireMfaForRoles` | `["Admin"]` | `["Admin"]` | `["Admin"]` (a customer may add `Lecturer`) |
| `Security:TrustForwardedHeaders` | true | true | `true` in `render.yaml` (Render's proxy is the only peer) — leave `false` if you ever put another proxy in front |
| `Security:AllowedHosts` | unset (`*`) | unset | optional; else falls back to `RENDER_EXTERNAL_HOSTNAME` |
| `Security:HstsIncludeSubDomains` / `HstsPreload` | n/a | n/a | false / false |
| `ASPNETCORE_ENVIRONMENT`, `ASPNETCORE_HTTP_PORTS`, `PORT` | Development, unset, unset (5080 via `run-api.ps1`) | Development (Production for a few header/KEK/cookie tests) | Production, `8080`, `8080` |

Rules that apply everywhere: no secret in git, in a Docker layer, or in a log line; `render.yaml` marks every secret
and every per-institution value `sync: false` so Render always prompts rather than silently keeping an old value;
the public demo's `Demo__*` values live only in the Render dashboard, never in the committed Blueprint.

## 5. Deploying for a customer (not the public demo)

Apply the same Blueprint. The differences from the public demo:

**Leave unset** (do not add these in the Render dashboard):
- `Demo__Enabled`
- `Demo__PublicDemoAcknowledged`
- `Database__SeedOnStartup`
- `Database__SeedStudentCount`

**Set:**
- `Bootstrap__AdminPassword` — a strong password satisfying the policy (12-128 characters, not the username, not
  "rushday", not on the embedded breached-password blocklist). Used once, to create the first administrator; remove
  it from the Render dashboard after that administrator has signed in and changed their password (the account is
  created with `must_change_password = true` regardless).
- `Bootstrap__AdminUsername` — only if `admin` was already used as a demo account during an earlier evaluation (it
  would then be a disabled demo account and the bootstrap step would refuse to reuse the name).
- `DataProtection__KeyEncryptionKey` — a fresh key, never reused from the public demo (section 7).
- `ConnectionStrings__RushDay` — the customer's own `rushday_app` connection string (section 6), never the owner
  role.
- `ConnectionStrings__Migrations` — optional; the customer's own owner-role string, if they want migrations to run
  on a different role than requests do.
- `Branding__InstitutionName`, `Branding__InstitutionShortName`, `Branding__TimeZone`, and optionally
  `Branding__PrivacyNoticeUrl` and `Branding__ResultsFootnote`.
- `Security__AllowedHosts` — the customer's own domain, if not using the default `onrender.com` subdomain.

The result: a database with roles, one `academic_settings` row, and one bootstrap administrator — no students, no
lecturers, no modules, no synthetic accounts. The administrator provisions everyone and everything else from the
admin UI (`docs/admin-guide.md`).

**Single sign-on**: v1 ships with local accounts and a TOTP second factor for administrators. ASP.NET Core Identity's
external-login mechanism is the extension point for Microsoft Entra ID or another SAML/OIDC provider — this is the
first request any UK university evaluating RushDay makes, and it is a Should for a reason: it needs an institution's
own tenant to test against, which this project does not have. `docs/admin-guide.md` names it as a documented,
unbuilt extension point rather than leaving it unmentioned.

## 6. The database role: `rushday_app`

The application never connects as the Neon owner role (`neondb_owner`). Run once, connected as the owner (Neon's SQL
editor, or `psql` against the owner connection string):

```sql
CREATE ROLE rushday_app LOGIN PASSWORD '<a strong, generated password>';
GRANT CONNECT ON DATABASE neondb TO rushday_app;
GRANT USAGE ON SCHEMA public TO rushday_app;
GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA public TO rushday_app;
GRANT USAGE, SELECT ON ALL SEQUENCES IN SCHEMA public TO rushday_app;
ALTER DEFAULT PRIVILEGES FOR ROLE neondb_owner IN SCHEMA public GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO rushday_app;
ALTER DEFAULT PRIVILEGES FOR ROLE neondb_owner IN SCHEMA public GRANT USAGE, SELECT ON SEQUENCES TO rushday_app;
```

Note what is deliberately **not** granted: `TRUNCATE`, any DDL (`CREATE`/`ALTER`/`DROP`), and trigger control. A
PostgreSQL row-level trigger (`trg_audit_events_immutable`) does not fire on `TRUNCATE`, so a role that could
truncate `audit_events` could erase the audit trail without ever hitting the trigger that blocks `UPDATE`/`DELETE`
on it — withholding `TRUNCATE` from `rushday_app` is what makes that trigger actually binding on the running
application, not just on a careless `DELETE`.

Use this connection string for `ConnectionStrings__RushDay`, with `SSL Mode=VerifyFull;Channel Binding=Require`.
Migrations run on `ConnectionStrings:Migrations` (the owner role) when that variable is set; when it is unset,
`MigrateAsync` runs on `ConnectionStrings:RushDay` instead (the application role), which works because migrations
also need DDL that role does not have — **residual risk**: a deployment that leaves `ConnectionStrings:Migrations`
unset has effectively granted `rushday_app` DDL for the migration to succeed, or must run migrations by hand as the
owner before each deploy. The recommended, and the public demo's, configuration sets `ConnectionStrings:Migrations`
to the owner role specifically so `rushday_app` never needs DDL rights at all.

## 7. The Data Protection key-encryption key (`DataProtection:KeyEncryptionKey`)

ASP.NET Core's Data Protection key ring (which backs both the session cookie and the antiforgery token) is stored in
the `data_protection_keys` table, encrypted at rest with AES-256-GCM under this key (D31). Generate 32 random bytes
and base64-encode them — **do not** use `Get-Random`, which is not a cryptographic RNG:

```powershell
[Convert]::ToBase64String([System.Security.Cryptography.RandomNumberGenerator]::GetBytes(32))
```

or, with OpenSSL on PATH:

```powershell
openssl rand -base64 32
```

Required in Production; startup aborts without it (`DataProtectionKeyEncryptionKey.MissingMessage`). Losing this
value invalidates every session and every antiforgery token in flight — nothing else. Rotating it is safe to do at
any time (it forces every signed-in user to log in again) and should be done if it is ever suspected to have leaked.
Store it only as a Render environment variable, never in git.

## 8. Rotate the database password

Procedure, used both routinely and whenever a password may have been exposed (this repository's own history: the
Neon password used during the very first deploy was pasted into a chat session and was rotated as a direct result —
see the rotation log below):

1. Neon console → the project → **Roles** → the role in use (`rushday_app`, or the owner role if rotating that
   instead) → reset password. Neon shows the new connection string once.
2. Render dashboard → the service → **Environment** → update `ConnectionStrings__RushDay` (and
   `ConnectionStrings__Migrations` if the owner role was rotated) with the new string.
3. Trigger a redeploy (Render redeploys automatically on an environment-variable change, or use **Manual Deploy**).
4. Confirm `GET /api/health/ready` answers 200 once the new deploy is live — this route specifically checks database
   reachability, so it is the right one to confirm the rotation did not break the connection string.

### Rotation log

| Date | What was rotated | Why |
|---|---|---|
| 2026-09-27 | Neon owner-role password (first deploy credential) | The password was shared in a chat session during initial setup and needed to be treated as exposed; rotated before any real data was stored. |
| *(pending)* | Neon password, ahead of stage S13's release | Routine rotation immediately before `v1` merges to `main` and starts carrying the live demo's real traffic — see `06-implementation-plan.md` stage S0. |

## 9. Backup and restore (Neon point-in-time restore)

Neon's free tier includes point-in-time restore for a rolling window (the exact number of days is a property of the
plan and is confirmed against the Neon console at the time of the rehearsal, not copied from memory, since free-tier
retention windows change). To rehearse a restore:

1. Neon console → the project → **Branches** → create a new branch from a point in time (or from the current head,
   for a rehearsal that does not depend on having an actual incident to restore from).
2. Note the branch's own connection string; point a throwaway `RUSHDAY_TEST_CONNECTION`-style check at it (never the
   application's real connection string) and confirm row counts match expectations (`SELECT count(*) FROM students`,
   `SELECT count(*) FROM grades`, etc.).
3. Record the date of the rehearsal, the outcome, and the restore window the free plan offered **on that day** below.
4. Delete the rehearsal branch (Neon branches count toward plan limits).

### Restore rehearsal record

*(Filled in during stage S13 — `06-implementation-plan.md` step 3: date, outcome, and the measured restore window
the free plan offered on that day.)*

| Date | Outcome | Restore window offered by the free plan on that day |
|---|---|---|
| *(pending)* | *(pending)* | *(pending — confirmed in the Neon console at rehearsal time, not assumed)* |

Because Data Protection keys live in `data_protection_keys` inside the same database, restoring the database also
restores session continuity, as long as the `DataProtection:KeyEncryptionKey` environment variable itself is kept
(it is not part of the database backup — it lives only in Render's environment).

## 10. Data residency

The public demo processes data in the EU: Render's web service runs in Frankfurt, and Neon's database is in AWS
`eu-central-1` (Frankfurt); this is covered by the UK's data-adequacy regulations for transfers between the UK and
the EU. Demo data is entirely synthetic (20,000 generated students, no real people). A customer deployment chooses
its own Render region and Neon region at setup and should record both here, alongside any data-processing agreement
reference relevant to that institution.

No IP address is ever stored: `audit_events.ip_hash` and the request-scoped log fields hold
`hex(HMAC-SHA256(dailyKey, ip))[..32]`, where `dailyKey` is itself derived from the key-encryption key and the UTC
date and is never persisted (D32) — the value cannot be reversed to an address without the key ring, and it changes
every day even for the same address.

## 11. Repository settings

Configured once, on GitHub, not through code (`06-implementation-plan.md` stage S0 and `docs/spec/03-security.md`
section 9):

- **Secret scanning with push protection**: enabled (Settings → Code security → Secret scanning; both the base
  feature and push protection). A commit containing a recognisable secret pattern is rejected before it reaches the
  remote.
- **Dependabot security updates**: enabled, alongside the version-update configuration already in
  `.github/dependabot.yml` (the `github-actions`, `docker`, `nuget` and `npm` ecosystems, weekly).
- **Branch protection on `main`**: required status checks `web`, `build-and-test`, and (from stage S12) `e2e`; no
  force pushes. Configured as part of the stage S13 pre-merge checklist, once those checks exist on `main`.

## 12. Troubleshooting

- **First request after idle takes 30-60 seconds**: Render's free web service spins down when idle and Neon
  suspends compute when idle; both wake on the first request. Not a fault — the README and the login page's
  cold-start notice say this plainly.
- **`503 server-busy` under load**: expected behaviour once the concurrency limiter's queue is full (ADR 10), not an
  outage — check `/api/admin/ops/metrics` (as an administrator) for `rushday.load_shed.rejected` counts.
- **A pooled connection surfaces one transient error right after a Neon resume**: expected (04-performance-and-ops.md
  section 5) — Neon can drop idle connections during a suspend/resume cycle; the API maps this to a single 503, and
  the next request succeeds normally. This is also why Production sets no `Keepalive`: keeping a connection alive
  specifically to avoid this would stop Neon's compute from ever auto-suspending, burning the free plan's monthly
  compute-hour budget for no measured benefit.
- **Startup aborts immediately**: check the log line — it will name exactly one of "demo mode without acknowledgement"
  or "missing key-encryption key" (section 7); both are deliberate, loud failures rather than a silent insecure
  fallback.
