# RushDay v1 specification: 02. API

Scope: every HTTP route, its role requirement, request and response JSON, status codes and error slugs; the
session, second-factor and antiforgery design; rate-limit policies; ProblemDetails conventions. Decisions D1–D2,
D4–D6, D13, D15–D17, D25 and D27–D32 of `00-overview.md` apply. Data shapes reference `01-domain-and-data.md`.

## 1. Conventions

- Every route is under `/api`. Legacy `/students/*`, `/modules`, `/health` and the root JSON index are removed; the
  SPA fallback serves `index.html` for every non-`/api` GET (`05-frontend.md` section 4).
- JSON: `PropertyNamingPolicy = CamelCase`; `JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues:
  false)` so `semester` is `"autumn" | "spring"`, `status` is `"draft" | "submitted" | "published"`, `outcome` is
  `"mark" | "absent" | "deferred"`, `day` is `"monday"`..; members travel as names only, and a number or a numeric
  string in a body (`"outcome": 1`, `"semester": "1"`, `"role": 0`) is 400 `validation`, never a silently chosen
  member (review S6 E13); nulls are emitted (`DefaultIgnoreCondition = Never`) so
  shapes are stable; `MaxDepth = 16`; unknown members ignored. Role names are the Identity strings
  `"Student" | "Lecturer" | "Admin"`.
- Instants are ISO-8601 UTC written with exactly three fractional digits (`2026-09-28T09:00:00.000Z`,
  `UtcDateTimeOffsetJsonConverter`); times of day `"HH:mm"`; ids are UUID strings. An instant in a request body must be a
  JSON string in ISO-8601 extended form (`yyyy-MM-ddTHH:mm:ss`, an optional fraction of up to seven digits, then `Z`, an
  offset or nothing for UTC), parsed with the invariant culture and normalised to UTC; any other token or text
  (`"not-a-date"`, `"28/09/2026 09:00"`, a number) is 400 `validation`.
- Paged responses: `{ items: T[], page: number, pageSize: number, total: number }`; `page` starts at 1;
  `pageSize` is clamped to 1..100 (roster and audit allow up to 200; marks up to 500). A page may end at row 10,000 at
  most: `page x pageSize` (with the clamped size) above 10,000 is 400 `validation` with `errors.page`
  (`PageRequest.MaxRows`, review S6 E14), because an `OFFSET` that deep costs a full count and scan per page; the filters
  (student, module, action, dates) and the CSV exports reach older rows, and the SPA's pagers stop at that page.
- Request validation: .NET 10 minimal API validation (`builder.Services.AddValidation()`) with DataAnnotations on
  request records. Invalid input → 400 `HttpValidationProblemDetails`, `type = urn:rushday:validation`, `errors`
  keyed by camelCase property. Every free-text query parameter (`q`, `actor`, `action`) carries `[StringLength(100)]`.
  A body that does not bind (malformed JSON, a wrong JSON type, an empty body) is 400 `validation` in every
  environment: `RouteHandlerOptions.ThrowOnBadRequest = false`, so Development does not turn it into a 500
  (`ProblemTypesTests.Unbindable_bodies_are_validation_problems`). Text PostgreSQL cannot store (a NUL character,
  SQLSTATE 22021) that gets past a validator is 400 `validation` too (the exception handler), never a 500; the
  validators are meant to stop it first (`POST /api/me/enrolments` takes only an ASCII module code). A list body with a
  `null` element (`{"rows":[null]}`, `{"assignments":[null]}`) is 400 `validation` (`NoNullElementsAttribute`; minimal API
  validation skips null items, so without it the handler dereferenced one: a 500, review S6 E11). Binding and validation run before the endpoint
  filters, so a malformed or invalid request answers 400 `validation` even without a valid antiforgery token or while a
  gate (section 2.3) would refuse it; neither discloses anything, and the filters still guard every request that binds.
- Query parameters holding a semester (`GET /api/admin/results`) are bound through a `SemesterQuery` record with
  `[RegularExpression("^(?i)(autumn|spring)$")]`: `autumn` or `spring`, case-insensitive, numeric values rejected with
  400 `validation`. JSON bodies use the enum converter (`"autumn" | "spring"`).
- Route constraints: `{studentNumber:regex(^S[0-9]{{6}}$)}`, `{code:regex(^[A-Z]{{2}}[0-9]{{4}}$)}`,
  `{staffNumber:regex(^L[0-9]{{5}}$)}`, `{id:guid}`. Digits are `[0-9]` in every route constraint and body pattern,
  never `\d`, which in .NET matches every Unicode decimal digit (Arabic-Indic, full-width): a path with such digits is
  no code and answers the `/api` fallback's 404 `not-found`. Module codes and student numbers in bodies are
  upper-cased server-side before lookup. Accepted (joint item J7): ASP.NET matches route constraints case-insensitively,
  and under that comparison the Kelvin sign (U+212A) equals `K`, so a code written with it in place of `K` passes `[A-Z]`; the
  handler upper-cases and looks up a code that no module has and answers 404 `module-not-found` (403 `not-your-module`
  under `/api/lecturer`), exactly as for any unknown code. It reaches no other module's data and discloses nothing, so
  no constraint change is made.
- Mutations (POST, PUT, DELETE) require the antiforgery header (section 3) and are subject to the `write` or a more
  specific rate-limit policy (section 5). Three GET routes that stream personal data (`GET /api/admin/audit/export.csv`,
  `GET /api/me/export.json`, `GET /api/admin/students/{n}/export.json`) are also under `write`.
- Every route may return 400 `validation`, 401 `unauthenticated`, 403 `forbidden`, 403 `password-change-required`,
  403 `mfa-setup-required`, 429 `rate-limited`, 503 `server-busy`, 503 `timeout`; these are not repeated per row below.
- Success bodies are exactly the shapes given; additive fields need a spec change.

## 2. Session design (ASP.NET Core Identity + cookie)

### 2.1 Identity configuration (exact)

```csharp
services.AddIdentityCore<ApplicationUser>(o =>
{
    o.Password.RequiredLength = 12;
    o.Password.RequireDigit = false; o.Password.RequireUppercase = false;
    o.Password.RequireLowercase = false; o.Password.RequireNonAlphanumeric = false;
    o.Password.RequiredUniqueChars = 4;
    o.Lockout.AllowedForNewUsers = true;
    o.Lockout.MaxFailedAccessAttempts = 5;
    o.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
    o.User.AllowedUserNameCharacters = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789._-";
    o.User.RequireUniqueEmail = false;
    o.ClaimsIdentity.RoleClaimType = RushDayClaims.Role;   // "role"
    o.ClaimsIdentity.UserIdClaimType = RushDayClaims.Subject; // "sub"
    o.ClaimsIdentity.UserNameClaimType = RushDayClaims.Name; // "name"
    o.Tokens.AuthenticatorTokenProvider = TokenOptions.DefaultAuthenticatorProvider;
})
.AddRoles<IdentityRole<Guid>>()
.AddEntityFrameworkStores<RushDayDbContext>()
.AddSignInManager()
.AddTokenProvider<ReplayProtectedAuthenticatorTokenProvider>(TokenOptions.DefaultAuthenticatorProvider) // TOTP only (no email/phone providers); section 2.4
.AddPasswordValidator<RushDayPasswordValidator>()
.AddClaimsPrincipalFactory<RushDayClaimsPrincipalFactory>();
services.Configure<PasswordHasherOptions>(o => o.IterationCount = 210_000);
services.AddScoped<ISecurityStampValidator, RushDaySecurityStampValidator>(); // interval from the svt claim; rejects disabled and locked-out users

var cookiePrefix = env.IsDevelopment() ? "" : "__Host-";   // Secure, Path=/ and no Domain already hold, so the prefix is free
var securePolicy = env.IsDevelopment() ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
services.AddAuthentication(IdentityConstants.ApplicationScheme).AddIdentityCookies();
services.ConfigureApplicationCookie(o =>
{
    o.Cookie.Name = cookiePrefix + "rushday.auth";
    o.Cookie.HttpOnly = true;
    o.Cookie.SameSite = SameSiteMode.Strict;
    o.Cookie.SecurePolicy = securePolicy;
    o.Cookie.Path = "/";
    o.ExpireTimeSpan = TimeSpan.FromHours(auth.SessionAbsoluteHours);   // 12: the cookie's own ceiling
    o.SlidingExpiration = false;                                          // sliding and absolute lifetimes are enforced per role in ValidateAsync
    o.Events.OnRedirectToLogin = ctx => ProblemResults.WriteAsync(ctx.HttpContext, 401, ProblemTypes.Unauthenticated);
    o.Events.OnRedirectToAccessDenied = ctx => ProblemResults.WriteAsync(ctx.HttpContext, 403, ProblemTypes.Forbidden);
    o.Events.OnValidatePrincipal = RushDayCookieEvents.ValidateAsync;
});
services.Configure<CookieAuthenticationOptions>(IdentityConstants.TwoFactorUserIdScheme, o =>
{
    o.Cookie.Name = cookiePrefix + "rushday.mfa";                          // set between password and code, 5 minutes
    o.Cookie.HttpOnly = true; o.Cookie.SameSite = SameSiteMode.Strict; o.Cookie.SecurePolicy = securePolicy; o.Cookie.Path = "/";
    o.ExpireTimeSpan = TimeSpan.FromMinutes(auth.MfaCookieMinutes);       // 5, from the password step
    o.SlidingExpiration = false;                                          // verify calls never extend the challenge
    o.Events.OnSigningIn = MfaChallengeBinding.OnSigningInAsync;          // binds the challenge to the security stamp
});
services.Configure<SecurityStampValidatorOptions>(o =>
{
    o.ValidationInterval = TimeSpan.FromMinutes(auth.SecurityStampIntervalMinutes); // 5
    o.OnRefreshingPrincipal = ctx => { RushDayCookieEvents.CopyLifetimeClaims(ctx.CurrentPrincipal, ctx.NewPrincipal); return Task.CompletedTask; };
});
services.AddDataProtection().SetApplicationName("RushDay").PersistKeysToDbContext<RushDayDbContext>();
if (!env.IsDevelopment())
{
    var kek = Convert.FromBase64String(config["DataProtection:KeyEncryptionKey"]
        ?? throw new InvalidOperationException("DataProtection__KeyEncryptionKey (32 random bytes, base64) is required outside Development."));
    services.AddSingleton(new DataProtectionKeyEncryptionKey(kek));
    services.Configure<KeyManagementOptions>(o => o.XmlEncryptor = new AesGcmXmlEncryptor(kek));
}
services.AddAuthorization(o =>
{
    o.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build();
    Policies.Register(o);
});
```

`Security/AesGcmXmlEncryptor : IXmlEncryptor` and `Security/AesGcmXmlDecryptor : IXmlDecryptor` (S2): the encryptor
serialises the key `XElement`, encrypts it with AES-256-GCM (random 12-byte nonce, 16-byte tag) and returns
`new EncryptedXmlInfo(<rushdayEncryptedKey keyId="{first 8 hex of sha256(kek)}" nonce="…" tag="…">base64</rushdayEncryptedKey>,
typeof(AesGcmXmlDecryptor))`; the decryptor takes `DataProtectionKeyEncryptionKey` from DI and refuses an element whose
`keyId` does not match (log Error `Data Protection key ring was encrypted under a different key`). Development keeps
plain keys. Losing the variable invalidates every session and antiforgery token and nothing else; the generation
command lives in `docs/deployment.md` (`06-implementation-plan.md` S11).

**Lifetimes** (`Auth/RushDayCookieEvents.cs`, `ValidateAsync`): the principal carries `iat` (sign-in instant),
`las` (last activity) and `svt` (last security-stamp check), all Unix seconds. Per role: `Student` and `Lecturer` get
`Auth:SessionSlidingHours` (8) and `Auth:SessionAbsoluteHours` (12); `Admin` gets `Auth:AdminSessionSlidingMinutes` (60)
and `Auth:AdminSessionAbsoluteHours` (8). `ValidateAsync`: (1) reject the principal (`RejectPrincipal()` +
`SignOutAsync`) when `now - iat > absolute` or `now - las > sliding`; (2) delegate to `ISecurityStampValidator.ValidateAsync`
(`RushDaySecurityStampValidator`), which is due when `now - svt > SecurityStampIntervalMinutes` (falling back to `iat`
when `svt` is missing) and then re-reads the user (one primary-key read per active session), rejects when the
security stamp changed (password change, reset, lock, disable, logout) and, through `VerifySecurityStamp` returning
null, when `disabled_at IS NOT NULL` or `lockout_end > now` (so an automatic lockout also ends existing sessions:
`AuthTests.Locked_out_user_session_dies_after_validation_interval`); on success it re-issues the principal with
`svt = now`. The interval is **never** measured from the ticket's `IssuedUtc`, as Identity's own validator does: step 3
renews the cookie (and so resets `IssuedUtc`) every minute of activity, which would postpone the check forever for a
session used at least every five minutes, leaving disable, lock, reset and logout effective only on idle sessions
(`SessionHardeningTests.Active_session_of_a_disabled_user_ends_within_the_interval`,
`SessionHardeningTests.Renewed_cookie_replayed_after_logout_is_rejected_within_the_interval`, both at the real 5-minute
interval). `iat` is preserved on every re-issue: `CopyLifetimeClaims` keeps the original `iat` and `las` when the
validator re-issues the principal (the factory would stamp a fresh `iat`, which must not restart the absolute clock),
and the factory itself keeps the current principal's `iat` when the same user is re-issued within a request
(`RefreshSignInAsync` after a password change or enabling MFA); (3) when `now - las > 60 s`, replace the principal
with one whose `las` is `now` (every other claim, `svt` included, unchanged) and set `ShouldRenew`. Password change
calls `UpdateSecurityStampAsync` then `RefreshSignInAsync` so the current session survives and every other session
dies at its next validation.

### 2.2 Claims (issued by `RushDayClaimsPrincipalFactory`)

| Claim | Value | Present for |
|---|---|---|
| `sub` | user id | all |
| `name` | username | all |
| `role` | `Student` / `Lecturer` / `Admin` | all (exactly one) |
| `display_name` | display name | all |
| `student_id`, `student_number` | uuid, `S000001` | Student |
| `lecturer_id`, `staff_number` | uuid, `L00001` | Lecturer |
| `pwd_change` | `1` | when `must_change_password` |
| `mfa_setup` | `1` | when the role is in `Auth:RequireMfaForRoles` (default `["Admin"]`), `two_factor_enabled` is false and the user is not (`is_demo` while `Demo:Enabled`) |
| `mfa` | `1` | when `two_factor_enabled` |
| `demo` | `1` | when `is_demo` |
| `iat`, `las` | Unix seconds at sign-in / last activity | all (preserved across re-issue, section 2.1) |
| `svt` | Unix seconds of the last security-stamp check (sign-in, then every successful re-check) | all (section 2.1) |

Ownership checks read claims (`CurrentUser` accessor wraps them); route values are never trusted for identity.
The factory always returns a principal; disabled, locked-out and demo-disabled users are rejected by
`RushDaySecurityStampValidator` and by login, never by the factory.

### 2.3 Login, logout, password change, forced change

`POST /api/auth/login` (`AuthEndpoints.Login`), in this order:

1. Antiforgery (group filter) and the named `login` policy (per client address, section 5) have already run.
2. `LoginThrottle.TryBegin(normalisedUsername, clientKey)`: reserves one permit in the per-address `login-failures`
   window and one in the per-username window, both of which count **failed outcomes only** by reserve-then-refund:
   the permits are taken before any work and handed back when the attempt succeeds (a right password, including the
   `mfaRequired` answer) or ends without an outcome (the CPU guard sheds it, an exception), so concurrent attempts can
   never overrun a window and a user's own successful sign-ins never spend it. Either window exhausted → 429
   `rate-limited` with `Retry-After`, metric `rushday.load_shed.rejected{policy=login}`; nothing else is touched, the
   password is not checked. `clientKey` is `RateLimitPolicies.ClientKey`: the IPv4 address (an IPv4-mapped IPv6 address
   in its IPv4 form), or a native IPv6 address truncated to its /64, because one host or subscriber routinely holds a
   whole /64; the full address is used only for the keyed `ipHash` of logs and audit rows.
3. `user = FindByNameAsync(username)`. When `user` is null, or `disabled_at IS NOT NULL`, or `user.IsDemo && !demo.Enabled`:
   run `PasswordHasher.VerifyHashedPassword(DummyUser, DummyHash, password)` so timing matches, record a failure in
   `LoginThrottle` (step 5), answer 401 `invalid-credentials`.
4. Acquire the CPU guard (`LoginThrottle.Cpu`, a `ConcurrencyLimiter`; exhausted → 429, `Retry-After: 2`) around
   `SignInManager.PasswordSignInAsync(user, password, isPersistent: false, lockoutOnFailure: false)` or the dummy verify
   of step 3, and release it as soon as that returns: failure accounting and the success completion (database round
   trips) run outside it (`LoginProtectionTests.Cpu_guard_is_released_before_the_sign_in_completes`).
   - `IsLockedOut` → run the dummy hash too (a locked account must not answer faster than a wrong password), then 401.
   - `RequiresTwoFactor` → Identity has set the `rushday.mfa` cookie; answer 200 `{ mfaRequired: true, csrfToken }`
     (`IAntiforgery.GetAndStoreTokens` for the anonymous identity). No claims are issued yet.
   - `Succeeded` → step 6.
   - otherwise (wrong password) → step 5, then 401.
5. Failure accounting (`LoginThrottle.RecordFailure(usernameHash, clientKey)`; the attempt keeps its window permits): a
   bounded in-process map of (`usernameHash`, `clientKey`) → last failure within a 15-minute window (at most 50,000
   entries, oldest evicted). When the user exists, is not `is_demo`, and the number of **distinct** client keys seen for
   that `usernameHash` in the window is at least `RateLimiting:LockoutDistinctIps` (3), call
   `UserManager.AccessFailedAsync(user)`; if that locks the account, audit `auth.locked_out { usernameHash, failedCount,
   ipHash }` and increment `rushday.auth.lockouts`. Single-address attacks never reach `AccessFailedAsync`: the
   per-address `login-failures` window (20 per 10 minutes) answers 429 first, and addresses within one IPv6 /64 are one
   address (`LoginProtectionTests.Addresses_in_one_ipv6_64_count_as_one_for_lockout`). Demo accounts are never counted
   toward lockout (their password is public; the per-username window still applies). Log line: `outcome`,
   `usernameHash`, `ipHash` only.
6. Success completion (shared with `POST /api/auth/mfa/verify`): `ResetAccessFailedCountAsync`, `last_login_at = now`,
   set `HttpContext.User` to the new principal, `IAntiforgery.GetAndStoreTokens(HttpContext)` so the returned
   `csrfToken` is bound to the signed-in identity, return `Me`. Metrics `rushday.auth.logins{outcome=success|failed|locked_out|mfa_required}`.

Wrong password, locked out, unknown user, disabled user and demo-disabled user all return the same 401
`invalid-credentials` in the same time; the body never distinguishes them
(`AuthTests.Unknown_user_wrong_password_and_locked_out_are_indistinguishable`,
`AuthTests.Demo_account_cannot_sign_in_when_demo_disabled`).

- `POST /api/auth/logout`: `UpdateSecurityStampAsync(user)` then `SignOutAsync(IdentityConstants.ApplicationScheme)`
  and `SignOutAsync(IdentityConstants.TwoFactorUserIdScheme)` → 204. The browser cookie is deleted and any copy of it
  is rejected at the next validation, at most `SecurityStampIntervalMinutes` after the copy's session was last checked,
  however often the copy was renewed (section 2.1); this also ends the account's sessions on other devices (the account
  page says so). The SPA then fetches a fresh anonymous token from `/api/auth/csrf`. Tests
  `AuthTests.Cookie_replayed_after_logout_is_rejected`, `SessionHardeningTests.Renewed_cookie_replayed_after_logout_is_rejected_within_the_interval`.
- `POST /api/auth/change-password` (`login` CPU guard plus the `password-change` limiter, section 5): 409
  `demo-account` when `is_demo`; 400 `weak-password` (`errors.newPassword = ["same-as-current"]`) when `newPassword`
  equals `currentPassword`; 400 `invalid-current-password` without checking anything when the account is locked out
  (a session outlives its lockout until the next stamp check and must not keep guessing); `ChangePasswordAsync` (on
  `PasswordMismatch`: `UserManager.AccessFailedAsync` so lockout applies, and when that locks the account audit
  `auth.locked_out { usernameHash, failedCount, ipHash }` in the same transaction and increment
  `rushday.auth.lockouts`; then 400 `invalid-current-password`; 400 `weak-password` with `errors.newPassword[]` when
  policy fails), clear `must_change_password`, `RefreshSignInAsync`, sign out the `TwoFactorUserId` scheme, audit
  `auth.password_changed { forced }` → 204 (`SessionHardeningTests.Change_password_lockout_is_audited_and_stops_further_guesses`).
- `MustChangePasswordFilter` and `MfaSetupRequiredFilter` (endpoint filters on the `/api` group, in that order): each
  is skipped when the endpoint carries `IAllowAnonymous` metadata (so `GET /api`, `/api/public/*`, `/api/health/*`,
  `/api/openapi/*`, `/api/auth/csrf`, `/api/auth/login`, `/api/auth/mfa/verify` and the fallbacks are unaffected) or the
  route is `GET /api/auth/me`, `POST /api/auth/logout` or `POST /api/auth/change-password`. Otherwise, a principal with
  `pwd_change=1` receives 403 `password-change-required`; then a principal with `mfa_setup=1` receives 403
  `mfa-setup-required` unless the route is under `/api/auth/mfa/`. Tests `AuthTests.Must_change_user_can_read_public_status`,
  `AuthTests.Admin_without_mfa_is_gated`. Like antiforgery (section 3) the gates are endpoint filters, so binding and
  validation run before them: a gated principal sending an invalid body gets 400 `validation`, not the 403 of the gate.
- No self-service reset (no email channel). Administrators reset via `POST /api/admin/accounts/{id}/reset-password`.
- `POST /api/auth/register` does not exist and `MapIdentityApi` is never called; the `/api/{**rest}` fallback is
  `AllowAnonymous`, so the route answers 404 `not-found` as ProblemDetails without a session.

### 2.4 Second factor (TOTP; the `/api/auth/mfa` group mapped in `Endpoints/AuthEndpoints.cs`)

`Auth/ReplayProtectedAuthenticatorTokenProvider`, registered as `TokenOptions.DefaultAuthenticatorProvider`: RFC 6238
(SHA-1, 30-second steps, 6 digits) over the key Identity stores (`GetAuthenticatorKeyAsync`), accepting ±2 steps
(about 90 s of skew either way) exactly like Identity's `AuthenticatorTokenProvider`, against the **real** clock (the
code comes from the user's phone; a test's fake clock never moves it). Unlike Identity's provider it records the last
accepted time step per user (`user_tokens` row `[RushDay]` / `LastTotpStep`, claimed by one atomic
`INSERT … ON CONFLICT … DO UPDATE … WHERE value < @step`) and refuses any step at or before it, so a code seen once
(shoulder-surfed, phished, replayed from a proxy log) signs nobody in, on `verify` and `enable` alike, and two
concurrent requests with one code cannot both pass (`SessionHardeningTests.Totp_code_is_refused_on_second_use`,
`SessionHardeningTests.Authenticator_provider_accepts_each_time_step_once`). The `TwoFactorUserId` cookie scheme that
`AddIdentityCookies()` registers carries the user between the password and the code: it lives exactly
`Auth:MfaCookieMinutes` (5) from the password step (`SlidingExpiration = false`, so verify calls never extend it) and
carries the account's security stamp at the password step (`MfaChallengeBinding`, set in the scheme's `OnSigningIn`);
`verify` answers 401 `invalid-credentials` and drops the cookie when the stamp has changed since (a password reset or
change, an MFA reset, a lock or disable) (`SessionHardeningTests.Mfa_challenge_expires_five_minutes_after_the_password_step`,
`SessionHardeningTests.Password_reset_invalidates_an_outstanding_mfa_challenge`). Logout and a password change also sign
the scheme out. No email or SMS channel is needed. Students never enrol a factor (D27): `setup` and `enable` answer 403
`forbidden` for the `Student` role (`SessionHardeningTests.Students_cannot_enrol_a_second_factor`).

| Method and route | Auth | Request | Response | Codes |
|---|---|---|---|---|
| `POST /api/auth/mfa/setup` | authenticated staff (allowed while `mfa_setup=1`) | | `{ sharedKey: string (base32, grouped in fours), otpauthUri: string }` (`ResetAuthenticatorKeyAsync` then `GetAuthenticatorKeyAsync`; issuer `Branding:InstitutionShortName`, label the username); audit `account.mfa_setup_started` | 200; 403 `forbidden` (role `Student`); 409 `demo-account` (`is_demo`); 409 `mfa-already-enabled` when `two_factor_enabled` |
| `POST /api/auth/mfa/enable` | authenticated staff | `{ code: /^[0-9]{6}$/ }` | `Me` (fresh claims via `RefreshSignInAsync`: `mfa=1`, no `mfa_setup`); `VerifyTwoFactorTokenAsync` (a step not used before) then `SetTwoFactorEnabledAsync(true)`; audit `account.mfa_enabled` | 200; 400 `invalid-mfa-code`; 403 `forbidden` (role `Student`); 409 `demo-account`, `mfa-already-enabled` |
| `POST /api/auth/mfa/verify` | anonymous route (`AllowAnonymous`) + the `rushday.mfa` cookie; `login` policy and `LoginThrottle` windows apply | `{ code: /^[0-9]{6}$/ }` | `Me`; the challenge's stamp must still be the account's, then `SignInManager.TwoFactorAuthenticatorSignInAsync(code, isPersistent: false, rememberClient: false)`, then the success completion of section 2.3 step 6 | 200; 401 `invalid-credentials` (wrong or already-used code, expired or missing cookie, a stamp that changed since the password step; Identity counts a wrong code as an access failure, so lockout applies after 5) |
| Should: `POST /api/auth/mfa/recovery-codes` | authenticated, `mfa=1` | | `{ codes: string[10] }` shown once (`GenerateNewTwoFactorRecoveryCodesAsync`); `POST /api/auth/mfa/verify` accepts `{ recoveryCode }` as an alternative | 200 |

`Auth:RequireMfaForRoles` (default `["Admin"]`): a user in a listed role whose `two_factor_enabled` is false signs in
normally, receives `Me.mfaSetupRequired = true`, and can reach only the exempt routes and `/api/auth/mfa/*` until
`enable` succeeds (403 `mfa-setup-required` elsewhere). `is_demo` users are exempt while `Demo:Enabled` (the demo
administrator is usable out of the box; it can never enable a factor, so the demo cannot be locked by a visitor).
An administrator resets another account's factor with `POST /api/admin/accounts/{id}/reset-mfa` (section 8.5), after
which the user is gated again at the next request. Test `AuthTests.Mfa_login_round_trip` (provision a non-demo
administrator, setup, enable with a code computed by the test's RFC 6238 helper from `sharedKey`, logout, login →
`mfaRequired`, verify → `Me` with the code of a later step than enable used (`Totp.FreshCode`); a wrong code → 401).

## 3. Antiforgery

```csharp
services.AddAntiforgery(o =>
{
    o.HeaderName = "X-CSRF-TOKEN";
    o.Cookie.Name = cookiePrefix + "rushday.csrf";     // "__Host-rushday.csrf" outside Development
    o.Cookie.HttpOnly = true;
    o.Cookie.SameSite = SameSiteMode.Strict;
    o.Cookie.SecurePolicy = securePolicy;
    o.Cookie.Path = "/";
    o.SuppressXFrameOptionsHeader = true; // SecurityHeadersMiddleware sets it
});
```

- `AntiforgeryEndpointFilter` on the whole `/api` group calls `IAntiforgery.ValidateRequestAsync` for POST, PUT,
  PATCH and DELETE. Failure → 400 `urn:rushday:antiforgery`. No exemptions: login and MFA verify are protected too
  (login CSRF). It is an endpoint filter, so body binding and request validation (section 1) run first: a request
  whose body does not bind or validate answers 400 `validation` whether or not its token is valid. That order is
  accepted: the answer reveals nothing and changes nothing, and every request that reaches a handler has passed the
  filter.
- The request token is delivered only in JSON bodies: `GET /api/auth/csrf` (anonymous), `POST /api/auth/login`
  (both response shapes), `POST /api/auth/mfa/verify` and `GET /api/auth/me` (`csrfToken` field). Tokens are bound to
  the principal, so the SPA refreshes after login, MFA verification and logout; on a 400 `antiforgery` it refreshes
  once and retries once.
- k6 does the same two calls and parses whatever cookies the responses set, whatever their names
  (`04-performance-and-ops.md` section 8).

## 4. Authorization policies (`Auth/Policies.cs`)

| Policy | Requirement | Failure |
|---|---|---|
| `StudentOnly` | role `Student` and claim `student_id` present | 403 `forbidden` |
| `LecturerOnly` | role `Lecturer` and claim `lecturer_id` present | 403 `forbidden` |
| `AdminOnly` | role `Admin` | 403 `forbidden` |
| `TeachesModule` | `TeachesModuleRequirement` evaluated by `TeachesModuleHandler` against route value `code`: passes when the caller has a `lecturer_id` claim and `LecturerModuleCache.GetCodesAsync(lecturerId)` (HybridCache 60 s, generation-versioned, invalidated by `PUT /api/admin/modules/{code}/lecturers` and by the lecturer's leave) contains the code. Nobody else passes. Unknown code still evaluates the cache (empty set) so the answer is 403, never 404, for a non-member. A lecturer who has left (`lecturers.left_at` set) teaches nothing: the cache fill, `StaffModules.TaughtAsync` (every lecturer handler and `MarksService`) and `GET /api/lecturer/modules` all require `left_at IS NULL`, so their assignments stay on record (with `left: true`) but carry no authority (review S6 E5). Used only under `/api/lecturer`. | 403 `not-your-module` |

There is no `Staff` policy. Structural rules that make cross-tenant reads impossible rather than merely forbidden:

- Student routes (`/api/me/*`) never take a student number; every query predicate uses `CurrentUser.StudentId`.
- Lecturer roster and marks queries join through `module_lecturers` (`WHERE ml.lecturer_id = @me AND m.code = @code`)
  so a policy bug yields an empty result, not another module's data.
- Only `/api/admin/*` accepts student numbers or account ids in the URL, and the group carries
  `.RequireAuthorization(Policies.AdminOnly)`.
- Fallback policy = authenticated; anonymous routes opt out with `.AllowAnonymous()` (section 8.1 lists them).
- An `Admin` cannot use student or lecturer routes (no `student_id`/`lecturer_id` claim; `AuthorizationMatrixTests`
  asserts an admin session on `GET /api/lecturer/modules` and `PUT /api/lecturer/modules/CS3099/marks` → 403
  `forbidden`). **Administrators never enter or submit marks**; correction goes through
  `POST /api/admin/results/modules/{code}/marks/{studentNumber}/correct` and return-to-draft plus the module's
  lecturers. They read any module's roster and marks sheet through the read-only
  `GET /api/admin/modules/{code}/roster` and `/marks` (D33). Admin actions on students go through explicit override
  routes that are audited with `override: true`.
- Within a module, only the `Leader` may submit marks (`MarksService.SubmitAsync` checks `module_lecturers.role`;
  a `Teacher` receives 403 `not-module-leader`, `AuthorizationMatrixTests.Teacher_cannot_submit_marks`).

## 5. Rate limiting

`Microsoft.AspNetCore.RateLimiting` with numbers bound from `RateLimiting:*` so Render can tune them by environment
variable; Development values are relaxed so k6 can authenticate hundreds of students from one machine. ASP.NET's
`RequireRateLimiting(name)` accepts one named policy partitioned on one key, and `PartitionedRateLimiter.CreateChained`
is usable only as the global limiter, so the login protection is split between one named policy on the endpoint and
`Auth/LoginThrottle.cs`, a singleton injected into `AuthEndpoints.Login` and `MfaVerify` that owns two in-process
"failed outcomes only" windows (`Auth/SlidingWindowCounter.cs`, a segmented sliding window whose permits can be
refunded, which `System.Threading.RateLimiting` cannot do) and the CPU guard. Every per-address key is
`RateLimitPolicies.ClientKey`: the client address after `ForwardedHeaders`, IPv4 as is (an IPv4-mapped IPv6 address in
its IPv4 form), native IPv6 truncated to its /64 (a single host or subscriber routinely holds a whole /64).

| Policy | Where | Partition key | Limiter | Production | Development | Rejection |
|---|---|---|---|---|---|---|
| global | every `/api` route **except** `/api/health/live` and `/api/admin/ops/metrics` | single | Concurrency, `OldestFirst` | permit 24, queue 96 | permit 64, queue **1,024** (deliberately larger than the 500-VU rush so `enrolment-rush.js` shows 30/470, not 503s) | 503 `server-busy`, `Retry-After: 1`, metric `rushday.load_shed.rejected{policy=api}` |
| `login` (named policy, `RequireRateLimiting`) | `POST /api/auth/login`, `POST /api/auth/mfa/verify` | client address key | Sliding window 60 s, 6 segments | `LoginPerIpPerMinute` 600 | 100,000 | 429 `rate-limited`, `Retry-After` from the lease |
| `login-failures` (`LoginThrottle`) | same handlers, reserved before any lookup, **counts failed outcomes only** (reserve-then-refund, section 2.3 step 2) | client address key | Sliding window 10 min, 10 segments | `LoginFailuresPerIpPer10Minutes` 20 | 100,000 | 429, `Retry-After` |
| per-username (`LoginThrottle`) | same handlers, reserved before any lookup, **counts failed outcomes only** | normalised username from the body (or the MFA cookie's user) | Sliding window 60 s, 6 segments | `LoginPerUserPerMinute` 10 | 100,000 | 429, `Retry-After` |
| CPU guard (`LoginThrottle.Cpu`) | around `PasswordSignInAsync` or the dummy hash (released before the sign-in completes) and `ChangePasswordAsync` | single | `ConcurrencyLimiter(permit = LoginConcurrency, queue = LoginQueue, OldestFirst)` | 8 / 16 | 64 / 512 | 429 `rate-limited`, `Retry-After: 2`, metric `rushday.load_shed.rejected{policy=login}` |
| `password-change` | `POST /api/auth/change-password` | `sub` claim | Fixed window 1 min | `PasswordChangePerUserPerMinute` 5 | 1,000 | 429 |
| `enrol` | `POST/DELETE /api/me/enrolments*` | `sub` claim | Token bucket | 5 tokens, +5 per 10 s | 1,000 | 429 |
| `write` | every other mutation, plus the three export GETs | `sub` claim (client address key when anonymous) | Token bucket | 120 tokens, +120 per minute | 10,000 | 429 |
| `health-ready` | `GET /api/health/ready` (inside the global limiter) | client address key | Fixed window 1 min | `HealthReadyPerIpPerMinute` 30 | 100,000 | 503 `server-busy`, `Retry-After: 60` (a probe answers "busy", not "rate-limited"); `RateLimitTests.Ready_probe_is_limited` |
| `ops-metrics` | `GET /api/admin/ops/metrics` (outside the global limiter) | `sub` claim | Token bucket | 1 token, +1 per 2 s | same | 429 |

Identity lockout (5 failures, 15 minutes, only when failures arrive from ≥ 3 addresses, IPv6 counted by /64) is the
per-account brake; the per-username window of failures is the spray brake; the per-address failure window stops a
single machine from locking accounts it does not own; the concurrency guard protects PBKDF2 CPU on 0.1 vCPU. The global
`/api` concurrency limiter also applies to login, and the guard's permits plus queue (8 + 16) stay below its 24
permits, so a login storm can occupy at most that many of them and never sheds every other `/api` request. Both
failure windows reserve their permit before the password is checked and refund it on success, so concurrent attempts
cannot overrun either window (`LoginProtectionTests.Concurrent_failures_cannot_overrun_the_per_address_window`) and the
owner's own sign-ins never spend the per-username window (`LoginProtectionTests.Successful_logins_do_not_spend_the_per_username_window`).
Every 429 and 503 carries `Retry-After` in whole seconds and a ProblemDetails body.

## 6. ProblemDetails

`AddProblemDetails(o => o.CustomizeProblemDetails = ctx => { ... })` sets `traceId` (`Activity.Current?.Id ??
HttpContext.TraceIdentifier`) on every problem and fills `type` when the framework produced the problem
(400 → `validation`, 401 → `unauthenticated`, 403 → `forbidden`, 404 → `not-found`, 405 → `method-not-allowed`,
413 → `payload-too-large`, 415 → `unsupported-media-type`, 429 → `rate-limited`, 500 → `internal-error`,
503 → `server-busy`). `UseExceptionHandler()` (no exception text), `UseStatusCodePages()`, `AddRequestTimeouts`
(15 s default → 503 `timeout`, `Retry-After: 2`). A transient `NpgsqlException` → 503 `server-busy`, `Retry-After: 2`;
the metric `rushday.db.pool_wait_timeouts` is incremented only for Npgsql's pool-exhaustion exception (`The connection
pool has been exhausted`, inner `TimeoutException`), not for a command timeout on a lock wait (also an inner
`TimeoutException`) and not for a dead pooled connection after a Neon resume. A `PostgresException` with SQLSTATE
22021 (text the database cannot store) → 400 `validation`.

The `/api/{**rest}` fallback outranks the framework's method and media-type answers: a route that exists but not for
the method or body type (`PUT /api/auth/login`, a form-encoded POST, and `HEAD` on a GET route, since minimal APIs map
GET only) answers 404 `not-found` as ProblemDetails rather than 405 or 415. The `method-not-allowed` and
`unsupported-media-type` slugs stay in the catalogue for the framework paths that do not fall through to it
(`ProblemTypesTests.Framework_problems_carry_catalogue_types_and_trace_ids`).

Shape: `{ type: "urn:rushday:<slug>", title, status, detail, instance, traceId, ...extensions }`. `detail` is safe to
show to the caller. The slug catalogue is closed; adding one is a spec change:

| Status | Slug | Where |
|---|---|---|
| 400 | `validation`, `antiforgery`, `invalid-current-password`, `weak-password`, `invalid-mfa-code` | any / auth |
| 401 | `unauthenticated`, `invalid-credentials` | any / login, MFA verify |
| 403 | `forbidden`, `not-your-module`, `not-module-leader`, `password-change-required`, `mfa-setup-required` | any / lecturer submit / gates |
| 404 | `not-found`, `module-not-found`, `student-not-found`, `lecturer-not-found`, `account-not-found`, `window-not-found`, `publication-not-found`, `announcement-not-found`, `grade-not-found`, `not-enrolled` | as named |
| 405 | `method-not-allowed` | framework |
| 409 | `already-enrolled`, `module-full`, `module-inactive`, `enrolment-window-closed` (`semester`, `opensAt`, `closesAt` extensions; both instants null when no window exists), `withdrawal-deadline-passed` (`withdrawalDeadlineAt`), `results-exist`, `student-left`, `module-locked`, `module-not-submitted`, `already-submitted`, `nothing-to-submit`, `stale-mark` (`studentNumbers[]`), `nothing-to-publish`, `publication-live`, `publication-scheduled`, `username-taken`, `principal-has-account`, `principal-left`, `module-code-taken`, `student-number-taken`, `staff-number-taken`, `window-exists`, `demo-account`, `mfa-already-enabled` | as named |
| 413 | `payload-too-large` | Kestrel body limit |
| 415 | `unsupported-media-type` | framework |
| 422 | `credit-limit-exceeded` (`currentCredits`, `moduleCredits`, `limit`, `semester`), `marks-incomplete` (`missing[]`), `not-enrolled-students` (`studentNumbers[]`), `capacity-below-enrolled` (`enrolledCount`), `semester-change-with-enrolments` (`enrolledCount`), `publish-too-far-ahead`, `invalid-lecturer-assignment`, `window-dates-invalid`, `self-lockout`, `role-principal-mismatch` | as named |
| 429 | `rate-limited` | limiters |
| 500 | `internal-error` | unhandled |
| 503 | `server-busy`, `timeout` | shedding, pool, request timeout |

`ProblemTypes` (S2) declares exactly these 63 slugs as constants; `ProblemTypesTests` asserts the set equals this table
and `05-frontend.md` section 6.4 maps every one of them. `principal-left` (409, account provisioning and enabling for a
student or lecturer who has left) was added by the S6 review (E5).

## 7. Shared shapes

```ts
Me { id: string; username: string; displayName: string; role: 'Student' | 'Lecturer' | 'Admin';
     studentNumber: string | null; staffNumber: string | null; mustChangePassword: boolean;
     mfaEnabled: boolean; mfaSetupRequired: boolean; isDemo: boolean; csrfToken: string }

MfaChallenge { mfaRequired: true; csrfToken: string }          // POST /api/auth/login when the user has a second factor
LoginResponse = Me | MfaChallenge                                // discriminated by the presence of `mfaRequired`

TimetableEntry { moduleCode: string; moduleTitle: string; semester: 'autumn' | 'spring';
                 day: 'monday'|'tuesday'|'wednesday'|'thursday'|'friday'|'saturday'|'sunday';
                 startTime: string; endTime: string; room: string;                        // "09:00"
                 kind: 'lecture' | 'lab' }                                                // 'lab' when room contains "-Lab"

WindowInfo { id: string; academicYear: string; semester: 'autumn' | 'spring'; opensAt: string; closesAt: string;
             withdrawalDeadlineAt: string; state: 'notYetOpen' | 'open' | 'closed' }

PublicationBrief { academicYear: string; semester: 'autumn' | 'spring'; publishAt: string; state: 'scheduled' | 'live' }

PublicationInfo = PublicationBrief & { id: string; gradeCount: number; moduleCount: number; createdAt: string;
                                       createdBy: string | null; note: string | null }

Lecturer { staffNumber: string; fullName: string; title: string; role: 'leader' | 'teacher'; left: boolean }

ModuleSummary { code: string; title: string; department: string; level: 1 | 2 | 3; credits: number;
                semester: 'autumn' | 'spring'; capacity: number;
                enrolledCount: number; placesRemaining: number;          // current academic year (modules.enrolled_count); placesRemaining = max(0, capacity - enrolledCount)
                isActive: boolean; lecturers: Lecturer[];
                enrolmentState: 'notYetOpen' | 'open' | 'closed' | 'noWindow'; windowOpensAt: string | null;
                windowClosesAt: string | null; withdrawalDeadlineAt: string | null }

ModuleDetail = ModuleSummary & { description: string | null; timetable: TimetableEntry[] }

GradeResult { moduleCode: string; moduleTitle: string; credits: number; semester: 'autumn' | 'spring'; academicYear: string;
              outcome: 'mark' | 'absent' | 'deferred'; mark: number | null; publishedAt: string;   // mark is null iff outcome != 'mark'
              correctedAt: string | null }                                                      // set when an administrator corrected it after submission

AnnouncementView { id: string; scope: 'university' | 'module'; moduleCode: string | null; title: string; body: string;
                   pinned: boolean; publishedAt: string; expiresAt: string | null; author: string;
                   createdAt: string; updatedAt: string }

MarksStatus { status: 'noStudents' | 'draft' | 'submitted' | 'scheduled' | 'published'; entered: number; missing: number;
              total: number; submittedAt: string | null; publishedAt: string | null }   // publishedAt = the publication's publish_at for scheduled and published

AuditEventView { id: string; occurredAt: string; actorUsername: string | null; actorRole: string | null; action: string;
                 subjectType: string; subjectId: string | null; studentNumber: string | null; moduleCode: string | null;
                 details: Record<string, unknown> | null; requestId: string | null }

AccountView { id: string; username: string; displayName: string; role: 'Student' | 'Lecturer' | 'Admin';
              studentNumber: string | null; staffNumber: string | null; email: string | null;
              state: 'active' | 'locked' | 'disabled'; lockoutEnd: string | null; mustChangePassword: boolean;
              mfaEnabled: boolean; isDemo: boolean; createdAt: string; lastLoginAt: string | null }

MyEnrolment { moduleCode: string; title: string; credits: number; semester: 'autumn' | 'spring'; academicYear: string;
              status: 'active' | 'withdrawn'; enrolledAt: string; withdrawnAt: string | null;
              canWithdraw: boolean; withdrawBlockedReason: 'deadline' | 'results' | 'year' | null; withdrawalDeadlineAt: string | null }

Paged<T> { items: T[]; page: number; pageSize: number; total: number }
```

`enrolmentState` and window instants on `ModuleSummary` come from the cached windows for the current academic year and
the module's semester. `AccountView.state = disabled_at != null ? 'disabled' : lockout_end > now ? 'locked' : 'active'`.

`MarksStatus` is computed for **one academic year** (the settings year on lecturer routes; `academicYear` on admin
results): `total` counts active enrolments of that year on the module; `status` from the grades joined to those
enrolments: `published` if any is Published with `published_at <= now`, else `scheduled` if any is Published with
`published_at > now`, else `submitted` if any is Submitted, else `draft` if any grade exists or `total > 0`, else
`noStudents`; `entered` counts the active enrolments whose grade belongs to the module's stage: while `status =
'draft'` any grade row (any outcome), and once the module has left draft only a Submitted or Published grade;
`missing = total - entered`. A Draft on an active enrolment of a module that has left draft (a student withdrawn before
the submit and enrolled again, which section 8.3's `module-locked` rule now prevents, or data from before it) is
therefore `missing` (review S6 E1). Grades of withdrawn enrolments are ignored everywhere in this computation. A module
is **publishable** iff `status = 'submitted' AND missing = 0`, that is, iff every active enrolment's grade is Submitted:
a publish moves exactly the Submitted grades, so it can never report a module published while leaving an active
student's mark behind.

`MyEnrolment.canWithdraw = status = 'active' AND academicYear = current year AND now < withdrawal_deadline_at AND no
grade for (student, module) with status IN ('Submitted', 'Published')`; `withdrawBlockedReason` names the first failing
condition (`year`, `deadline`, `results`) and is null when `canWithdraw` or when `status = 'withdrawn'`.

## 8. Route table

### 8.1 Public (`.AllowAnonymous()`, no rate limit other than global unless stated)

Every endpoint in this table, the `/api/{**rest}` 404 fallback, `MapFallbackToFile("index.html")` and the 503
"Front end not built" endpoint carry `.AllowAnonymous()`; nothing else does.

| Method and route | Response | Codes |
|---|---|---|
| `GET /api` | `{ name: "RushDay", story: <the story sentence pair>, commit: string, environment: string, links: { health: "/api/health/live", ready: "/api/health/ready", status: "/api/public/status", login: "/api/auth/login", github: "https://github.com/Ahmeddayyan/RushDay", openapi?: "/api/openapi/v1.json" } }`; `openapi` is present only in Development | 200 |
| `GET /api/health/live` | `{ status: "Healthy" }` (no dependencies; outside every limiter) | 200 |
| `GET /api/health/ready` (`health-ready` limiter, inside global) | `{ status: "Healthy" \| "Unhealthy", checks: [{ name: "database", status, durationMs }] }` (`AddDbContextCheck`) | 200, 503 |
| `GET /api/public/status` | `{ serverTime, institution: { name, shortName, timeZone, privacyNoticeUrl: string \| null, resultsFootnote: string, support: { email: string \| null, url: string \| null } \| null }, academicYear, currentSemester: 'autumn' \| 'spring', nextPublication: PublicationBrief \| null, latestPublication: PublicationBrief \| null, enrolmentWindows: WindowInfo[], demo: { accounts: [{ role, username, password, hint }] } \| null }`; `nextPublication` = earliest `publish_at > now`, `latestPublication` = latest `publish_at <= now`; `privacyNoticeUrl` and `resultsFootnote` come from `Branding:*` configuration at request time; `support` comes from `academic_settings.support_email`/`support_url` (null when both are null); `hint` is the static text of `01-domain-and-data.md` section 7; output-cached 10 s under the named policy `public-status`, a custom `IOutputCachePolicy` (`PublicStatusCachePolicy`: `AddOutputCache(o => o.AddPolicy("public-status", new PublicStatusCachePolicy()))`; GET and HEAD only, 10 s, locking on, and never stored when the response is not a 200 or sets a cookie), so signed-in callers are served from cache too (the default policy, which refuses authenticated requests, is not part of it): the response never varies by user and sets no cookie (`PublicStatusTests.Signed_in_callers_are_served_the_same_uncookied_response` advances the clock a second between the two calls and checks the `Age` header); `demo` is null unless `Demo:Enabled` | 200 |
| `GET /api/openapi/v1.json` | OpenAPI document (`MapOpenApi("/api/openapi/{documentName}.json")`), **mapped only when `env.IsDevelopment()`**; 404 `not-found` elsewhere | 200 |
| `GET /api/auth/csrf` | `{ csrfToken }`; sets `rushday.csrf` | 200 |
| `POST /api/auth/login` (`login` policy + `LoginThrottle`) | body `{ username: string (1..64), password: string (1..128) }` → `LoginResponse`; sets `rushday.auth` (or `rushday.mfa` when `mfaRequired`) | 200; 401 `invalid-credentials`; 429 |
| `POST /api/auth/mfa/verify` (`login` policy + `LoginThrottle`) | body `{ code }` → `Me`; sets `rushday.auth`, clears `rushday.mfa` | 200; 401; 429 |

### 8.2 Authenticated, any role

| Method and route | Request | Response | Codes |
|---|---|---|---|
| `GET /api/auth/me` | | `Me` (fresh `csrfToken`) | 200 |
| `POST /api/auth/logout` | | | 204 |
| `POST /api/auth/change-password` (`password-change`, CPU guard) | `{ currentPassword, newPassword }` | | 204; 400 `invalid-current-password`, `weak-password`; 409 `demo-account` |
| `POST /api/auth/mfa/setup`, `POST /api/auth/mfa/enable` | section 2.4 | | |
| `GET /api/modules` | | `ModuleSummary[]` — every `is_active` module ordered by code; viewer-agnostic; served from `catalogue:all` (30 s) | 200 |
| `GET /api/modules/{code}` | | `ModuleDetail` — the module row is read uncached so `enrolledCount` is live; an inactive module is returned too (`isActive: false`) so a link from a dashboard or an old enrolment never dead-ends | 200; 404 `module-not-found` |
| `GET /api/announcements` | | `AnnouncementView[]` visible now (`published_at <= now`, not expired, not deleted), filtered: Student → university + modules with an active enrolment in the current academic year; Lecturer → university + assigned modules; Admin → all; pinned first, then `publishedAt` desc; max 50 | 200 |

### 8.3 Student (`StudentOnly`; group `/api/me`)

| Method and route | Request | Response | Codes |
|---|---|---|---|
| `GET /api/me/dashboard` | | `DashboardResponse` (below) | 200 |
| `GET /api/me/results` | | `{ semesters: [{ academicYear: string; semester; state: 'published' \| 'scheduled' \| 'pending'; publishAt: string \| null; results: GradeResult[] }], weightedAverage: number \| null, classification: string \| null }`, newest year first, autumn before spring | 200 |
| `GET /api/me/timetable` | | `TimetableEntry[]` for active enrolments of the current academic year on modules of `academic_settings.current_semester` | 200 |
| `GET /api/me/enrolments` | | `MyEnrolment[]`, every academic year, current year first then `enrolledAt` desc (the catalogue labels earlier years "Completed") | 200 |
| `POST /api/me/enrolments` (`enrol`) | `{ moduleCode: /^\s*[A-Za-z]{2}[0-9]{4}\s*$/ }` (trimmed and upper-cased) | `{ moduleCode, enrolledAt, placesRemaining }` (no `Location` header: there is no single-enrolment GET) | 201; 404 `module-not-found`; 409 `already-enrolled`, `module-full`, `module-inactive`, `enrolment-window-closed`, `results-exist`, `student-left`, `module-locked` (the module's marks for this year have left draft, below); 422 `credit-limit-exceeded` |
| `DELETE /api/me/enrolments/{code}` (`enrol`) | | | 204; 404 `not-enrolled`; 409 `withdrawal-deadline-passed`, `results-exist` |
| `GET /api/me/export.json` (`write`) | | `StudentExport` = `{ student, enrolments, grades: GradeResult[], weightedAverage, classification, exportedAt }`: `student` and `enrolments` as in `AdminStudentView`; `grades` the visible grades only (no drafts, and no `status`, `version` or `visibleToStudent`: they describe states the student never sees, T6); `Content-Disposition: attachment; filename="rushday-{studentNumber}.json"`; audit `student.exported_self` | 200 |

**Joining a module whose marks have left draft** (review S6 E1). Once a module's `MarksStatus` for the current year is
`submitted`, `scheduled` or `published` (an active enrolment of the year holds a Submitted or Published grade), nobody
joins it: a new enrolment or a reactivation, the student's own or an administrator's override (section 8.5), answers
409 `module-locked` (detail "Marks for this module have already been submitted this year, so nobody can join it until
the academic office returns them to draft."). The newcomer's mark could otherwise never be reached: lecturers are
locked out of a submitted module, a correction needs a submitted grade, a publish moves only Submitted grades and a
live module cannot be returned to draft. The registry returns the module to draft first (a scheduled one included;
only a live one needs its publication unpublished), enrols the student, and the lecturers enter the mark and resubmit;
a student who was withdrawn with a draft finds that draft again. The check runs in the enrolment transaction under the
module's marks lock taken **shared** (`ModuleMarksLock.AcquireSharedAsync`, `04-performance-and-ops.md` section 2.1
step 5b), so a submit, return to draft or publish of the module either committed before it or waits for the enrolment;
enrolments do not wait for one another. It is not a rush rejection reason (no `rushday.enrolments.rejected` tag). A
withdrawal is unaffected.

```ts
DashboardResponse {
  studentNumber: string; fullName: string; programme: string; yearOfStudy: number; academicYear: string;
  currentSemester: 'autumn' | 'spring';
  modules: [{ code; title; credits; semester; academicYear; enrolledAt; canWithdraw: boolean;
              withdrawBlockedReason: 'deadline' | 'results' | 'year' | null; withdrawalDeadlineAt: string | null }];   // active enrolments of the current year
  completed: [{ code; title; credits; semester; academicYear; outcome: 'mark' | 'absent' | 'deferred' | null;
                mark: number | null; band: string | null }];                                                       // active enrolments of earlier years; mark/band only when visible
  timetable: TimetableEntry[];                      // whole week, current-year modules of the current semester; the SPA derives "today"
  results: GradeResult[];                           // visible grades only, every year
  weightedAverage: number | null; classification: string | null;
  credits: { autumn: number; spring: number; limit: 60 };   // current year, active enrolments
  nextPublication: PublicationBrief | null;         // earliest scheduled publication in the future
  latestPublication: PublicationBrief | null;       // most recent live publication
  enrolmentWindows: WindowInfo[];                   // current academic year
  announcements: AnnouncementView[];                // latest 5 visible, pinned first
}
```

`results.semesters[]` is grouped by (`enrolments.academic_year`, `modules.semester`) through the grade → enrolment join:
`state` is `published` when at least one visible grade exists for that pair; else `scheduled` when the student has grades
on that pair with `status = Published AND published_at > now` (`publishAt` = the earliest such instant; the mark itself
is never sent); else `pending` when the student has an active enrolment in that pair. Pairs with neither are omitted.
`weightedAverage` and `classification` use visible grades with `outcome = mark` only (`Classification.Graded`,
`Classification.WeightedAverage`, `Classification.FromAverage`); `band` on `completed[]` is `Classification.Band(mark)`.
"Visible" always means `GradeQueries.VisibleToStudents(db, now)`: `g.status = 'Published' AND g.published_at <= @now
AND EXISTS (SELECT 1 FROM enrolments e WHERE e.student_id = g.student_id AND e.module_id = g.module_id AND e.status =
'Active')`. Student routes read `grades` only through `GradeQueries`: marks only through this predicate
(`VisibleResultsFor`), scheduled instants without marks through `ScheduledInstantsFor`, and whether a Submitted or
Published grade exists (`results-exist`, `canWithdraw`) through `WithResults`; `AdminStudentView.grades[].visibleToStudent`
uses the same predicate.

### 8.4 Lecturer (`LecturerOnly`; group `/api/lecturer`; `TeachesModule` wherever `{code}` appears; `write` limiter on mutations; every read scoped to the current academic year)

| Method and route | Request | Response | Codes |
|---|---|---|---|
| `GET /api/lecturer/modules` | | `[ModuleSummary & { myRole: 'leader' \| 'teacher'; marks: MarksStatus }]` | 200 |
| `GET /api/lecturer/modules/{code}/roster` | `q?` (≤100), `page=1`, `pageSize=50` (≤200) | `{ module: ModuleSummary; items: [{ studentNumber; fullName; programme; yearOfStudy; status: 'active' \| 'withdrawn'; enrolledAt; withdrawnAt }]; page; pageSize; total }` active first, then by student number | 200; 403 `not-your-module` |
| `GET /api/lecturer/modules/{code}/marks` | `q?` (≤100), `page=1`, `pageSize=100` (≤500) | `MarksSheet` (below): `rows` is the page, `summary` covers the whole module | 200; 403 |
| `PUT /api/lecturer/modules/{code}/marks` | `{ rows: [{ studentNumber; mark: number \| null; outcome?: 'mark' \| 'absent' \| 'deferred' (default `mark`); version: number \| null }] }` (1..500 rows; `mark` required 0..100 integer when `outcome = mark`, must be null otherwise) | `{ summary: MarksSheet['summary']; rows: MarksSheet['rows'] }` where `rows` are exactly the submitted students' rows as stored after the save (new `version`, `updatedAt`, `enteredBy`), in request order | 200; 403; 409 `module-locked`, `stale-mark`; 422 `not-enrolled-students` |
| `POST /api/lecturer/modules/{code}/marks/submit` | | `{ code; status: 'submitted'; submittedAt; gradeCount }` | 200; 403 `not-your-module`, `not-module-leader`; 409 `already-submitted`, `module-locked` (scheduled or published), `nothing-to-submit` (no active enrolment this year); 422 `marks-incomplete` |
| `GET /api/lecturer/modules/{code}/announcements` | | `AnnouncementView[]` (module scope, including future and expired, not deleted) | 200; 403 |
| `POST /api/lecturer/modules/{code}/announcements` | `{ title (1..120); body (1..4000); pinned?: boolean; publishedAt?: string; expiresAt?: string }` | `AnnouncementView` | 201; 403 |
| `PUT /api/lecturer/modules/{code}/announcements/{id}` | same body | `AnnouncementView` | 200; 403; 404 `announcement-not-found` |
| `DELETE /api/lecturer/modules/{code}/announcements/{id}` | | | 204; 403; 404 `announcement-not-found` |
| Should: `GET /api/lecturer/modules/{code}/roster.csv` (`write`) | | `text/csv` attachment `roster-{code}.csv` (at most 10,000 rows); a bulk read of personal data, so it writes `roster.exported { moduleCode, academicYear, rowCount }` (subject the module, the lecturer as actor), committed before the file is sent (review S6 E15) | 200; 403 |

The `q` predicate (roster, and reused by admin students and accounts): `student_number ILIKE @q || '%' OR full_name
ILIKE '%' || @q || '%'` with `%` and `_` escaped, so a surname finds "Aisha Khan".

```ts
MarksSheet {
  code: string; title: string; academicYear: string; status: MarksStatus['status']; submittedAt: string | null; publishedAt: string | null;
  myRole: 'leader' | 'teacher' | null;     // null on the admin read route
  leader: string | null;                   // leader's display name, for "Ask {leader} to submit"
  summary: { entered: number; missing: number; total: number };
  rows: [{ studentNumber: string; fullName: string; enrolmentStatus: 'active' | 'withdrawn';
           outcome: 'mark' | 'absent' | 'deferred' | null; mark: number | null; gradeStatus: 'draft' | 'submitted' | 'published' | null;
           version: number | null; updatedAt: string | null; enteredBy: string | null; correctedAt: string | null }];   // rows ordered active first, then by studentNumber
  page: number; pageSize: number; total: number
}
```

`PUT marks` rules (`MarksService.SaveAsync`, one transaction, all-or-nothing per request): module status must be
`draft` or `noStudents` (else 409 `module-locked`); every `studentNumber` must have an **active** enrolment in the
current academic year (else 422 `not-enrolled-students`); for existing grade rows `version` must equal the stored
version (else 409 `stale-mark`, nothing saved); new rows are created as `Draft` with `version = 1`; changed rows get
`version + 1`, `updated_at`, `entered_by_user_id`; an unchanged row is not written; every insert audits `grade.entered`
and every change `grade.changed` with `{ before, after }` (each side `{ mark, outcome }`). Metric `rushday.grades.saved`
+= rows written. The SPA sends dirty rows as sequential requests of at most 500 rows each. Teachers and the leader may
save.

`submit` rules: the caller must hold `role = 'Leader'` on the module (else 403 `not-module-leader`, detail `Only the
module leader can submit marks`); the module must have at least one active enrolment in the current year (else 409
`nothing-to-submit`); every active enrolment of the current year must have a Draft grade with a mark or a non-mark
outcome (else 422 `marks-incomplete` with `missing: string[]`); sets exactly those grades to `Submitted` with
`submitted_at` and `version + 1`; audit `module.marks_submitted` with `{ gradeCount, academicYear }`. Only grades of
**active** enrolments are submitted: a withdrawn student's draft stays Draft, is not counted in `marks-incomplete`, and
can never be published.

Announcement rules: the announcement of `PUT`/`DELETE` is resolved with `WHERE id = @id AND scope = 'Module' AND
module_id = @moduleIdOfCode AND deleted_at IS NULL`; any other row (another module's, a university one, a deleted one)
answers 404 `announcement-not-found`, so the route's `{code}` is the only way in
(`AuthorizationMatrixTests.Lecturer_cannot_edit_announcement_of_another_module`,
`AuthorizationMatrixTests.Lecturer_cannot_delete_university_announcement`).

### 8.5 Administrator (`AdminOnly`; group `/api/admin`; `write` limiter on mutations)

**Overview and settings**

| Method and route | Request | Response | Codes |
|---|---|---|---|
| `GET /api/admin/overview` | | `{ counts: { students; lecturers; modules; activeEnrolments; accounts; lockedAccounts; disabledAccounts }; academicYear; enrolmentWindows: WindowInfo[]; nextPublication: PublicationInfo \| null; latestPublication: PublicationInfo \| null; submissionProgress: [{ semester; modulesTotal; noStudents; draft; submitted; scheduled; published }]; recentAudit: AuditEventView[] (10); database: 'ok' \| 'degraded' }`; `activeEnrolments` and `submissionProgress` cover the current academic year; `modulesTotal` excludes `noStudents`; `database` is the result of the same `DbContext` check as `/api/health/ready`, run in-process (not through the rate-limited route) | 200 |
| `GET /api/admin/settings` | | `{ academicYear; currentSemester: 'autumn' \| 'spring'; institutionName; institutionShortName; timeZone; supportEmail: string \| null; supportUrl: string \| null; updatedAt }` | 200 |
| `PUT /api/admin/settings` | `{ academicYear: /^[0-9]{4}\/[0-9]{2}$/; currentSemester; institutionName (1..200); institutionShortName (1..32); timeZone; supportEmail? (email, ≤256); supportUrl? (absolute https URL, ≤400) }` | same as GET | 200; audit `settings.changed`; invalidates `settings`, and when `academicYear` changed runs the `reconcile_enrolled_count` statements in the same transaction and invalidates `catalogue:all` and `windows:all` |

`timeZone` validation: `TimeZoneInfo.TryFindSystemTimeZoneById(id, out _)` when the runtime has ICU, otherwise (when
`AppContext.TryGetSwitch("System.Globalization.Invariant", out var inv) && inv`, which is the case on this project's
builds) the regex `^[A-Za-z]+(/[A-Za-z_+-]+){1,2}$`. The server stores the id as text; only the browser formats with it.

**Enrolment windows**

| Method and route | Request | Response | Codes |
|---|---|---|---|
| `GET /api/admin/enrolment-windows` | | `WindowInfo[]` (all years, newest first) | 200 |
| `POST /api/admin/enrolment-windows` | `{ academicYear; semester; opensAt; closesAt; withdrawalDeadlineAt }` | `WindowInfo` | 201; 409 `window-exists`; 422 `window-dates-invalid` |
| `PUT /api/admin/enrolment-windows/{id}` | `{ opensAt; closesAt; withdrawalDeadlineAt }` | `WindowInfo` | 200; 404 `window-not-found`; 422 |
| `DELETE /api/admin/enrolment-windows/{id}` | | | 204; 404 |

Each mutation invalidates `windows:all` and audits `window.created|updated|deleted`.

**Results**

| Method and route | Request | Response | Codes |
|---|---|---|---|
| `GET /api/admin/results` | `semester` (required, `SemesterQuery`), `academicYear?` (defaults to settings) | `{ academicYear; semester; modules: [{ code; title; leader: string \| null; enrolledCount; marks: MarksStatus }]; publications: PublicationInfo[] }` sorted by `marks.status` (`noStudents` last) then code; `enrolledCount` = `marks.total` (active enrolments of that year) | 200 |
| `POST /api/admin/results/publish` | `{ academicYear; semester; publishAt; announce: boolean; note? (≤400) }` | `{ publication: PublicationInfo; published: { modules: number; grades: number }; excluded: [{ code; status: 'draft' \| 'submitted'; reason: 'notSubmitted' \| 'marksMissing'; marksMissing: number }] }` | 200; 409 `nothing-to-publish` (no publishable module); 422 `publish-too-far-ahead` (> 90 days ahead) |
| `PUT /api/admin/results/publications/{id}` | `{ publishAt }` | `PublicationInfo` | 200; 404 `publication-not-found`; 409 `publication-live`; 422 `publish-too-far-ahead` |
| `DELETE /api/admin/results/publications/{id}` | | `{ academicYear; semester; grades: number }` | 200; 404; 409 `publication-live` |
| `POST /api/admin/results/publications/{id}/unpublish` | `{ reason (10..400) }` | `{ academicYear; semester; grades: number }` | 200; 404; 409 `publication-scheduled` (use DELETE) |
| `POST /api/admin/results/modules/{code}/return-to-draft` | `{ reason (10..400); academicYear? }` | `{ code; status: 'draft'; fromScheduledPublication: boolean }` | 200; 404 `module-not-found`; 409 `module-not-submitted` (status draft or noStudents), `module-locked` (published and live) |
| `POST /api/admin/results/modules/{code}/marks/{studentNumber}/correct` | `{ outcome?: 'mark' \| 'absent' \| 'deferred' (default `mark`); mark: number \| null (0..100 integer when `outcome = mark`, else null); reason (10..400) }` | `{ studentNumber; before: { mark; outcome }; after: { mark; outcome }; version; correctedAt }` | 200; 400 `validation` (`errors.mark`) when the mark and the outcome are the ones already recorded; 404 `module-not-found`, `grade-not-found`; 409 `module-not-submitted` (the grade is still Draft: lecturers own it) |

`publish` rules (`ResultsPublicationService.PublishAsync`, one transaction): `publishAt` earlier than now is
replaced by now (the SPA's picker never offers a past instant); compute `MarksStatus` for every module of the (year,
semester); the candidates are the publishable modules (`status = 'submitted' AND missing = 0`, section 7); none → 409
`nothing-to-publish`; take the candidates' marks locks exclusively in module-id order (`ModuleMarksLock.AcquireManyAsync`)
and compute their `MarksStatus` again, so an enrolment, a return to draft or a save that ran meanwhile has either
committed (and is seen) or waits for the publish (review S6 E1, E9); `@publishable` = the candidates still publishable;
none → 409 `nothing-to-publish`; insert `results_publications`; `UPDATE grades g SET status = 'Published', published_at =
@publishAt, publication_id = @id, updated_at = now(), version = g.version + 1 FROM enrolments e WHERE e.student_id =
g.student_id AND e.module_id = g.module_id AND e.academic_year = @academicYear AND e.status = 'Active' AND g.module_id
= ANY(@publishable) AND g.status = 'Submitted'`; `excluded` lists the modules of the (year, semester) that are `draft`
(`reason: 'notSubmitted'`) or `submitted` with `missing > 0` (`reason: 'marksMissing'`: an active enrolment without a
Submitted grade, which the `module-locked` rule of section 8.3 keeps from arising through the API; the fix is
return-to-draft); `noStudents`, `scheduled` and `published` modules are not listed. When `announce`, insert a pinned
university announcement titled `{Semester} {academicYear} results are available` (for example `Autumn 2026/27 results
are available`), body `Sign in to see your marks.` followed by a blank line and `Branding:ResultsFootnote`, with
`published_at = publishAt` and no formatted date in the text (D26), store its id in `results_publications.announcement_id`
and audit `announcement.created`; audit `results.published` with counts; metric `rushday.results.published`; invalidate
`publications:brief` (and `announcements:university` when announcing) after the commit. Partially entered marks are
never published and there is no flag to include drafts. Calling publish again for the same (year, semester) publishes
newly publishable modules under a new publication row.

**The announcement follows its publication** (review S6 E2): a publish's "results are available" announcement must
never appear before the results or outlive them, so every change to the publication carries it along in the same
transaction, audited: a reschedule moves its `published_at` to the new instant (`announcement.updated`); a cancel, an
unpublish and a return to draft that empties the publication soft-delete it (`announcement.deleted`). An administrator
may still edit or delete it on the announcements page like any other; a deleted one is left alone.

Reschedule (`PUT publications/{id}`) locks the row, updates it and `UPDATE grades SET published_at = @new WHERE
publication_id = @id`, and moves the announcement, only while `publish_at > now`; audit `results.rescheduled`. Cancel
(`DELETE`) is allowed only while scheduled: `UPDATE grades SET status = 'Submitted', published_at = NULL,
publication_id = NULL, updated_at = now(), version = version + 1 WHERE publication_id = @id`, delete the announcement and
the row, audit `results.cancelled { academicYear, semester, grades }`. Unpublish is allowed only while live: the same
grade update, delete the announcement and the row, audit `results.unpublished { academicYear, semester, grades, reason
}`; students stop seeing those marks, and the announcement, immediately. Return-to-draft: allowed when the module's
grades of that year are Submitted, or Published under a publication with `publish_at > now` (then it also decrements
that publication's `grade_count`/`module_count`, sets `fromScheduledPublication = true`); sets them to Draft with
`submitted_at = NULL`, `published_at = NULL`, `publication_id = NULL`, `version + 1`; audit `module.returned_to_draft {
reason, gradeCount, academicYear, fromScheduledPublication }`. A scheduled publication left with no grade by it is
deleted in the same transaction with its announcement and audited `results.cancelled { academicYear, semester, grades:
0, returnedToDraft: code }` (review S6 E3), so no empty publication drives the countdown or later becomes the latest
live one. It takes the module's marks lock, then the rows of the publications its grades belong to (`ORDER BY id FOR
UPDATE`), then the grade rows: the order a reschedule, cancel or unpublish takes them in (publication, then grades), so
it waits behind one instead of deadlocking with it (review S6 E9, `ResultsGovernanceTests`). When any grade of the
module is Published with `published_at <= now` the answer is 409 `module-locked` (unpublish the semester or correct
single marks instead). Correct: the grade of (`studentNumber`, `code`) must exist (else 404 `grade-not-found`) and be
Submitted or Published (else 409 `module-not-submitted`), and the request must change the mark or the outcome (else 400
`validation` with `errors.mark`, and nothing is written: a student never sees "Amended" on a mark nobody amended, review
S6 E12); sets `mark` and `outcome`, `version + 1`, `updated_at`, `corrected_at = now`, `entered_by_user_id = caller`;
keeps `status`, `published_at` and `publication_id`, so a published correction is visible to the student immediately
with its "Amended" label; audit `grade.corrected { moduleCode, studentNumber, before, after, reason }`. Every one of
these invalidates `publications:brief` after its commit, and a reschedule, cancel, unpublish or return to draft also
`announcements:university`.

**Students**

| Method and route | Request | Response | Codes |
|---|---|---|---|
| `GET /api/admin/students` | `q?` (≤100; the section 8.4 predicate), `accountState?` (`none`\|`active`\|`locked`\|`disabled`), `page`, `pageSize` | `Paged<{ studentNumber; fullName; programme; yearOfStudy; email; leftAt: string \| null; accountState: 'none' \| 'active' \| 'locked' \| 'disabled' }>` ordered by student number | 200 |
| `POST /api/admin/students` | `{ studentNumber: /^S[0-9]{6}$/; fullName (1..200); programme (1..200); yearOfStudy (1..6); email? }` | the row | 201; 409 `student-number-taken`; audit `student.created` |
| `GET /api/admin/students/{studentNumber}` | | `AdminStudentView` (below); **audited** `student.viewed { studentNumber }` (subject Student) | 200; 404 `student-not-found` |
| `PUT /api/admin/students/{studentNumber}` | `{ fullName; programme; yearOfStudy; email }` | the row | 200; 404; 409 `demo-account` (a demo actor, while the linked account is a real one: the edit would rename a real login, review S6 E6); audit `student.updated { before, after }`; updates the linked user's `display_name` in the same transaction (a demo account's too: a display name is not a credential and cannot lock a visitor out, so it follows its record like any other) |
| `POST /api/admin/students/{studentNumber}/leave` | `{ reason (10..400) }` | `{ studentNumber; leftAt; withdrawn: number }` | 200; 404; 409 `student-left` (already left), `demo-account` (the linked account is a demo account, or a real one and the actor is a demo actor); sets `left_at`, admin-withdraws the active enrolments of the current year that hold **no Submitted or Published grade** (review S6 E8: a mark already with the exam board stays visible to the student, in the export and to the registry, and a submitted one is published with the rest), through one `EnrolmentService.WithdrawManyAsync` call (`enrolment.admin_withdrawn` per row, `{ reason, override: true, left: true }`), disables the linked account (`account.disabled`), audit `student.left { reason }`; `withdrawn` counts the rows withdrawn |
| `GET /api/admin/students/{studentNumber}/export.json` (`write`) | | `StudentExport` (section 8.3) | 200; 404; audit `student.exported { studentNumber }` |
| `POST /api/admin/students/{studentNumber}/enrolments` | `{ moduleCode; reason (10..400); forceCapacity?: boolean }` | `{ moduleCode; enrolledAt; placesRemaining; capacityRaised: boolean }` | 201; 404 `student-not-found`, `module-not-found`; 409 `already-enrolled`, `results-exist`, `student-left`, `module-inactive`, `module-full` (without `forceCapacity`), `module-locked` (the module's marks for this year have left draft, section 8.3; return it to draft first) |
| `POST /api/admin/students/{studentNumber}/enrolments/{code}/withdraw` | `{ reason (10..400) }` | | 204; 404 `not-enrolled` |

Override rules: windows and the credit limit are ignored (`EnrolmentRules.Evaluate(..., windowOpen: true,
ignoreCreditLimit: true)`); `results-exist`, `student-left` and `module-locked` (section 8.3) still apply. Capacity applies unless `forceCapacity`,
which **raises capacity only when the module is full**: step 8 of `04-performance-and-ops.md` section 2.1 runs
`WITH before AS (SELECT capacity AS c FROM modules WHERE id = @module FOR UPDATE) UPDATE modules m SET enrolled_count =
m.enrolled_count + 1, capacity = CASE WHEN m.enrolled_count >= m.capacity THEN m.enrolled_count + 1 ELSE m.capacity END,
updated_at = @now FROM before WHERE m.id = @module RETURNING m.capacity, m.enrolled_count, m.capacity <> before.c AS
raised`; `capacityRaised = raised`; audits `module.updated` (`{ capacity: { before, after }, reason }`) only when
`raised`, and always `enrolment.admin_created` (`{ reason, override: true, forceCapacity, capacityRaised }`). Admin
withdrawal ignores the withdrawal deadline and the `results-exist` rule (the student then no longer sees that
module's mark, section 8.3) and audits `enrolment.admin_withdrawn` with the reason; it decrements `enrolled_count`
only when the withdrawn row belongs to the current academic year.

```ts
AdminStudentView {
  student: { studentNumber; fullName; programme; yearOfStudy; email; leftAt: string | null };
  account: AccountView | null;
  enrolments: [{ moduleCode; title; credits; semester; academicYear; status; source: 'seed' | 'self' | 'admin'; enrolledAt; withdrawnAt }];
  grades: [{ moduleCode; moduleTitle; credits; semester; academicYear; outcome; mark: number | null; status: 'draft' | 'submitted' | 'published'; publishedAt: string | null; visibleToStudent: boolean; version: number; correctedAt: string | null }];
  weightedAverage: number | null; classification: string | null;   // visible grades only, as the student sees it
  recentAudit: AuditEventView[];                                    // 10
}
```

**Modules and lecturers**

| Method and route | Request | Response | Codes |
|---|---|---|---|
| `GET /api/admin/modules` | `includeInactive?=false` | `[ModuleSummary & { description: string \| null; marks: MarksStatus }]` | 200 |
| `POST /api/admin/modules` | `{ code: /^[A-Z]{2}[0-9]{4}$/; title (1..200); description? (≤2000); credits (5..60); capacity (0..10000); semester }` | `ModuleDetail` | 201; 409 `module-code-taken` |
| `PUT /api/admin/modules/{code}` | `{ title; description; credits; capacity; semester; isActive }` | `ModuleDetail` | 200; 404; 422 `capacity-below-enrolled` **only when the request lowers `capacity` below `enrolled_count`**; an unchanged capacity is always accepted even while `enrolled_count > capacity`; 422 `semester-change-with-enrolments` (`enrolledCount` = the module's enrolments of **every** year, active or withdrawn) when `semester` differs and the module has any enrolment or any grade in any year: results are grouped by (enrolment year, module semester), so a change would move students' credits and timetables and earlier years' published marks into another semester (review S6 E7); the module row is locked `FOR UPDATE`, which also holds off a new enrolment's foreign-key check until the decision commits. A module that has never been used may move; a running course moves by creating a new module |
| `GET /api/admin/modules/{code}/roster` | `q?` (≤100), `page=1`, `pageSize=50` (≤200), `academicYear?` (defaults to settings) | the lecturer roster shape of section 8.4, for any module (no `module_lecturers` join) | 200; 404 `module-not-found` |
| `GET /api/admin/modules/{code}/marks` | `q?` (≤100), `page=1`, `pageSize=100` (≤500), `academicYear?` | `MarksSheet` with `myRole: null`, read-only (there is no admin `PUT` of marks; single marks are corrected through the results route) | 200; 404 `module-not-found` |
| `POST /api/admin/modules/{code}/trim-to-capacity` | `{ reason (10..400) }` | `{ code; capacity; before: number; after: number; withdrawn: string[] }` (`before`/`after`: the stored `enrolled_count` when the trim started and when it ended) | 200; 404; locks every student holding one of the year's places (id order, the enrolment lock order), counts the **real** active enrolments of the current year (never the stored counter: a drifted counter must not withdraw anyone from a module that is not over capacity, review S6 E4), withdraws the latest `enrolled_at` beyond capacity through one `EnrolmentService.WithdrawManyAsync` call (joint item J1; one `enrolment.admin_withdrawn { reason, override: true, trim: true }` per row), then sets `enrolled_count` to the real count under the module row lock (which repairs drift); audit `module.trimmed { reason, withdrawn, enrolledCount: { before, after } }` |
| `PUT /api/admin/modules/{code}/lecturers` | `{ assignments: [{ staffNumber; role: 'leader' \| 'teacher' }] }` (exactly one leader, no duplicates, no lecturer with `left_at`) | `ModuleDetail` | 200; 404 `module-not-found`, `lecturer-not-found`; 422 `invalid-lecturer-assignment`; the module row is locked (`FOR NO KEY UPDATE`) before the current assignments are read, so two concurrent assignments of one module run one after the other, and the unique index `ix_module_lecturers_module_id_leader` makes a second leader impossible at the database (review S6 E10); demotions and removals are saved before the new leader |
| `GET /api/admin/lecturers` | `q?` (≤100; staff number prefix or name fragment) | `[{ staffNumber; fullName; title; department; email; leftAt: string \| null; hasAccount: boolean; moduleCodes: string[] }]` | 200 |
| `POST /api/admin/lecturers` | `{ staffNumber: /^L[0-9]{5}$/; fullName; title (Dr\|Prof\|Mr\|Ms\|Mx); department (1..8); email? }` | the row | 201; 409 `staff-number-taken`; audit `lecturer.created` |
| `PUT /api/admin/lecturers/{staffNumber}` | `{ fullName; title; department; email }` | the row | 200; 404 `lecturer-not-found`; 409 `demo-account` (a demo actor, while the linked account is a real one, as for students); audit `lecturer.updated { before, after }`; updates the linked user's `display_name` |
| `POST /api/admin/lecturers/{staffNumber}/leave` | `{ reason (10..400) }` | the row | 200; 404; 409 `demo-account` (as for students); sets `left_at`, disables the linked account (`account.disabled`), audit `lecturer.left { reason }`; assignments stay and carry `left: true` but no authority (section 4: the lecturer teaches nothing), and `lecturer-modules:{lecturerId}` is invalidated after the commit; idempotent (a lecturer who already left is returned unchanged, no audit row) |

Module mutations invalidate `catalogue:all` and audit `module.created|updated|lecturers_set|trimmed`; changing
`credits` on a module with active enrolments is allowed and audited (the UI warns). Lecturer assignment invalidates
`lecturer-modules:{lecturerId}` for every lecturer added or removed.

**Accounts**

| Method and route | Request | Response | Codes |
|---|---|---|---|
| `GET /api/admin/accounts` | `q?` (≤100; username prefix or display name fragment), `role?`, `state?` (`active`\|`locked`\|`disabled`), `page`, `pageSize` | `Paged<AccountView>` | 200 |
| `POST /api/admin/accounts` | `{ username (1..64, allowed chars); displayName (1..200); role; studentNumber?; staffNumber?; email?; temporaryPassword? (policy) }` | `{ account: AccountView; temporaryPassword: string }` (generated 16-character password when omitted, alphabet in `01` section 8; shown once) | 201; 404 `student-not-found`, `lecturer-not-found`; 409 `username-taken`, `principal-has-account`, `principal-left` (the student or lecturer has left: a login would give a former lecturer their modules back, review S6 E5); 422 `role-principal-mismatch` |
| `POST /api/admin/accounts/{id}/lock` | | `AccountView` (`lockoutEnd = 9999-12-31T00:00:00Z`) | 200; 404 `account-not-found`; 409 `demo-account`; 422 `self-lockout` |
| `POST /api/admin/accounts/{id}/unlock` | | `AccountView` | 200; 404; 409 `demo-account` |
| `POST /api/admin/accounts/{id}/disable` | | `AccountView` | 200; 404; 409 `demo-account`; 422 `self-lockout` |
| `POST /api/admin/accounts/{id}/enable` | | `AccountView` | 200; 404; 409 `demo-account`, `principal-left` (the account's student or lecturer has left; the leave disabled it and it stays disabled) |
| `POST /api/admin/accounts/{id}/reset-password` | `{ temporaryPassword? }` | `{ temporaryPassword }` | 200; 404; 409 `demo-account`; `self-lockout` is not raised (admins may reset their own: the response is followed by the caller's session ending at the next request because the stamp rotated; the SPA shows the temporary password, then redirects to `/login`) |
| `POST /api/admin/accounts/{id}/reset-mfa` | | `AccountView` (`mfaEnabled = false`) | 200; 404; 409 `demo-account`; `SetTwoFactorEnabledAsync(false)`, `ResetAuthenticatorKeyAsync`, rotate the stamp; audit `account.mfa_reset { username }` |

Provision, reset, lock and disable set `must_change_password = true` (reset and provision), update the security
stamp (lock, disable, reset, reset-mfa) and audit `account.provisioned|locked|unlocked|disabled|enabled|password_reset|mfa_reset`.
A `Student` role requires `studentNumber` and a `Lecturer` role requires `staffNumber`; an `Admin` requires neither.
Every mutation on an `is_demo` account answers 409 `demo-account` (detail `Demo accounts are read-only`). A **demo
actor** (a session with the `demo` claim, whose password is on the login page; `IAuditContext.ActorIsDemo`) may not
mutate a non-demo account either (409 `demo-account`), so the public demo administrator can never lock, disable, reset
or re-enable a real account; and every account a demo actor provisions is created with `is_demo = true` (and without
`must_change_password`, since a demo account cannot change its password), so it is read-only, never locked out, and
disabled with the rest of the demo when demo mode is switched off (`DemoActorTests`). The same rule reaches the record
routes that change a login: a demo actor's student or lecturer edit (which renames the linked login) or leave (which
disables it) is 409 `demo-account` when the linked account is a real one (review S6 E6). No account is provisioned or
re-enabled for a student or lecturer who has left (409 `principal-left`, review S6 E5).

**Announcements (university scope)**

| Method and route | Request | Response | Codes |
|---|---|---|---|
| `GET /api/admin/announcements` | | `AnnouncementView[]` (all scopes, including future and expired, not deleted) | 200 |
| `POST /api/admin/announcements` | `{ title; body; pinned?; publishedAt?; expiresAt? }` | `AnnouncementView` | 201 |
| `PUT /api/admin/announcements/{id}` | same | `AnnouncementView` | 200; 404 |
| `DELETE /api/admin/announcements/{id}` | | | 204; 404 |

Admins may edit or delete announcements of any scope through these routes. Mutations invalidate
`announcements:university` and audit `announcement.created|updated|deleted`. `AnnouncementService`'s write methods take
an explicit `AnnouncementWriteScope` (joint item J3): `Administrator` for these routes and the results publication (a
new announcement is university-wide; an edit or a delete reaches any scope) or `Module(moduleId)` for the lecturer
routes, which obtain it only from the module resolved through `module_lecturers` (section 8.4); there is no null that
could mean "any scope". A results publication's announcement moves and goes with the publication (the results rules
above).

**Audit and ops**

| Method and route | Request | Response | Codes |
|---|---|---|---|
| `GET /api/admin/audit` | `actor?` (username, ≤100), `studentNumber?`, `moduleCode?`, `action?` (≤100), `from?`, `to?`, `page`, `pageSize` (≤200) | `Paged<AuditEventView>` newest first | 200 |
| `GET /api/admin/audit/export.csv` (`write`) | same filters | `text/csv; charset=utf-8`, attachment `audit-<from>-<to>.csv`, columns `occurredAt,actorUsername,actorRole,action,subjectType,subjectId,studentNumber,moduleCode,details,requestId`; row cap 50,000 when `from` and `to` are both given and span at most 31 days, otherwise 10,000; when the cap is hit the response carries `X-RushDay-Truncated: true` and a final line `# truncated at {cap} rows; narrow the date range`; an `audit.exported { filters, rowCount, truncated }` row (subject System) is written and committed **before** the CSV starts streaming | 200 |
| `GET /api/admin/ops/metrics` (`ops-metrics` limiter, outside the global limiter) | | `OpsSnapshot` (`04-performance-and-ops.md` section 6) | 200 |
| `POST /api/admin/ops/reconcile` | | `{ modulesCorrected: [{ code; before; after }] }` (runs the `reconcile_enrolled_count` statements; audits `ops.reconciled`) | 200 |
| `POST /api/admin/ops/demo-reset` | | `{ withdrawn: number }` (runs backfill step 11 `demo_reset_hot_module` and then the reconciliation; audits `system.demo_reset` with the caller as actor); **mapped only when `Demo:Enabled`**, so on a customer deployment the path answers the `/api` fallback's 404 `not-found` | 200 |

### 8.6 Removed routes

`GET /students/{n}/dashboard`, `POST /students/{n}/enrolments`, `GET /modules`, `GET /modules/{code}`, `GET /health`,
`GET /openapi/v1.json` and the interim `wwwroot/index.html`. `src/RushDay.Api/RushDay.Api.http` is rewritten for the
new routes (csrf → login → me → dashboard → enrol; a second section walks lecturer marks → submit → admin publish →
student results; it never expects a `Location` header from the enrol call).
