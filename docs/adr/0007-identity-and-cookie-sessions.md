# 7. ASP.NET Core Identity, cookie sessions over JWT, and a TOTP second factor for staff

Date: 2026-09-29

## Status

Accepted

## Context

I built RushDay because my own university's portal fell over every results day and enrolment window. I wanted to
understand why that happens and to build a portal that doesn't crash under the same load. The v0 baseline
(ADR 5) deliberately shipped with no authentication at all, so the load numbers it produced would be honest about
concurrency and correctness bugs rather than about a login form. v1 turns RushDay into something a university could
actually buy: three roles (student, lecturer, administrator), and a buyer's first question about any portal that
handles marks is "who can see what, and how do you know it was really them?"

Three login stores were on the table: roll a custom one, use ASP.NET Core Identity's own hashing and lockout with a
bespoke session, or take the Identity EF Core store wholesale including `SignInManager`, security stamps and TOTP.
Sessions had to be either a signed/encrypted cookie or a JWT; the SPA is served from the same origin as the API
(D25, one container, one URL), so there is no cross-origin case for a bearer token to solve.

## Decision

- **Identity store** (D1-D3): `ApplicationUser : IdentityUser<Guid>` with the EF Core store, tables renamed to
  snake_case. No `MapIdentityApi`, no registration route — the university provisions every account. A domain
  `lecturers` table mirrors `students`; `users.lecturer_id` links them, so a lecturer record can exist (and be
  taught by, assigned to modules, appear on rosters) without ever having a login, and domain data never depends on
  the login store.
- **Session** (D4): cookie authentication, `HttpOnly`, `Secure`, `SameSite=Strict`, `__Host-rushday.auth` outside
  Development (the `__Host-` prefix refuses a cookie set by anything other than this exact host over HTTPS, which
  stops a sibling subdomain planting one on a customer's domain). Students and lecturers get an 8-hour sliding /
  12-hour absolute session; administrators get 60 minutes sliding / 8 hours absolute, because an administrator's
  session can publish results or disable an account. The security stamp is re-validated every 5 minutes, so a lock,
  a disable, or a password reset takes effect within minutes instead of at the next login. Logout rotates the
  stamp, so every session of the account (not just the current cookie) dies at once. A bearer JWT was rejected
  outright: it needs script-readable storage (defeating the point of `HttpOnly`) and a denylist to revoke before
  expiry, both solved for free by a server-side, HttpOnly cookie on a same-origin SPA.
- **Antiforgery** (D5): every non-GET `/api` request, including login itself, carries `X-CSRF-TOKEN`. The token is
  returned only in JSON bodies (`/api/auth/csrf`, the login response, the MFA verify response, `/api/auth/me`),
  never as a script-readable cookie, so the SPA never parses a cookie to find it and k6 follows the same two calls
  any browser does.
- **Login throttling** (D6): one ASP.NET rate-limiting policy per client address (600/min) plus `LoginThrottle` in
  the handler itself: a per-username window of failed attempts (10/min) and a per-address window of failed
  attempts (20 per 10 minutes, both reserved before the password check and refunded on success), and a bounded
  concurrency guard around PBKDF2 verification (8 concurrent, 16 queued in production) because CPU, not the
  database, is the scarce resource on a 0.1 vCPU container. Identity's own lockout (5 failures / 15 minutes) only
  fires when the failing requests came from 3 or more distinct addresses, specifically so that a single attacking
  address gets stopped by the per-IP window without ever locking the account it is attacking — a naive "5 wrong
  passwords locks the account" rule turns an enumerable username (every student number is `S` + 6 digits) into a
  denial-of-service tool against the very roster of students it was meant to protect.
- **Second factor** (D27): TOTP (RFC 6238), Must for `Admin` — an administrator without one can reach only the MFA
  setup routes — Should for `Lecturer` (voluntary), never required for `Student` in v1. The public demo administrator
  is exempt from the MFA requirement only while demo mode is on, so a visitor can see the admin surface without
  provisioning an authenticator app first; a real deployment never gets that exemption.

## Consequences

- A demo visitor or a k6 script authenticates exactly the way a real user does: no header that bypasses the
  session, no backdoor route. `login-storm.js` exercises the same throttle a real credential-stuffing attempt
  would hit.
- The security-stamp re-validation costs one extra query per request once every 5 minutes per session, which is
  cheap next to what it buys: disabling an account is not advisory, it takes effect on the account's next request
  without waiting for the cookie to expire.
- The residual risk of D6 (a distributed spray from many addresses, each staying under its own per-IP window)
  is recorded in `docs/spec/03-security.md` and is out of scope for a free-tier, single-instance deployment with no WAF.
- `login-storm.js -Mode spray` and `-Mode guard -ProductionLoginGuard` are the load evidence for this decision
  (`04-performance-and-ops.md` section 8-9): the guard mode measures how many real logins per second the PBKDF2
  concurrency guard sustains at the production limiter settings, and the spray mode shows the per-IP failure
  window's onset and that a second address is never punished for the first address's attack.

v0 had no login at all, so there is no "before" load number to cite for authentication itself; the "before" here is
qualitative — every v0 route was reachable by anyone (`docs/load-results/2026-09-27-v0-baseline.md`, and the
optional `hotfix/v0-writes` branch of `06-implementation-plan.md` stage S0 that made the live v0 refuse
unauthenticated writes once this was understood).

**v1 evidence (stage S13):** `load/results/login-storm-guard-*.json`, `load/results/login-storm-spray-*.json` —
recorded in [`docs/load-results/2026-10-02-v1-hardened.md`](../load-results/2026-10-02-v1-hardened.md) run 4: guard mode 3,146 logins accepted and 3 asked to wait at up to 40/s, login p95 536 ms; spray mode 429 from the 21st failure from one address while the genuine student signed in from a second address.
