# RushDay v1 specification: 02. API

Scope: every HTTP route, its role requirement, request and response JSON, status codes and error slugs; the
session and antiforgery design; rate-limit policies; ProblemDetails conventions. Decisions D1–D2, D4–D6, D13,
D15–D17, D25 of `00-overview.md` apply. Data shapes reference `01-domain-and-data.md`.

## 1. Conventions

- Every route is under `/api`. Legacy `/students/*`, `/modules`, `/health` and the root JSON index are removed; the
  SPA fallback serves `index.html` for every non-`/api` GET (`05-frontend.md` section 4).
- JSON: `PropertyNamingPolicy = CamelCase`; `JsonStringEnumConverter(JsonNamingPolicy.CamelCase)` so
  `semester` is `"autumn" | "spring"`, `status` is `"draft" | "submitted" | "published"`, `day` is `"monday"`..;
  nulls are emitted (`DefaultIgnoreCondition = Never`) so shapes are stable; `MaxDepth = 16`; unknown members ignored.
  Role names are the Identity strings `"Student" | "Lecturer" | "Admin"`.
- Instants are ISO-8601 UTC (`2026-09-28T09:00:00Z`); times of day `"HH:mm"`; ids are UUID strings.
- Paged responses: `{ items: T[], page: number, pageSize: number, total: number }`; `page` starts at 1;
  `pageSize` is clamped to 1..100 (roster and audit allow up to 200).
- Request validation: .NET 10 minimal API validation (`builder.Services.AddValidation()`) with DataAnnotations on
  request records. Invalid input → 400 `HttpValidationProblemDetails`, `type = urn:rushday:validation`, `errors`
  keyed by camelCase property.
- Route constraints: `{studentNumber:regex(^S\d{{6}}$)}`, `{code:regex(^[A-Z]{{2}}\d{{4}}$)}`,
  `{staffNumber:regex(^L\d{{5}}$)}`, `{id:guid}`. Module codes and student numbers in bodies are upper-cased
  server-side before lookup.
- Mutations (POST, PUT, DELETE) require the antiforgery header (section 3) and are subject to the `write` or a more
  specific rate-limit policy (section 5).
- Every route may return 400 `validation`, 401 `unauthenticated`, 403 `forbidden`, 403 `password-change-required`,
  429 `rate-limited`, 503 `server-busy`, 503 `timeout`; these are not repeated per row below.
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
})
.AddRoles<IdentityRole<Guid>>()
.AddEntityFrameworkStores<RushDayDbContext>()
.AddSignInManager()
.AddPasswordValidator<RushDayPasswordValidator>()
.AddClaimsPrincipalFactory<RushDayClaimsPrincipalFactory>();

services.AddAuthentication(IdentityConstants.ApplicationScheme).AddIdentityCookies();
services.ConfigureApplicationCookie(o =>
{
    o.Cookie.Name = "rushday.auth";
    o.Cookie.HttpOnly = true;
    o.Cookie.SameSite = SameSiteMode.Strict;
    o.Cookie.SecurePolicy = env.IsDevelopment() ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
    o.Cookie.Path = "/";
    o.ExpireTimeSpan = TimeSpan.FromHours(auth.SessionSlidingHours);   // 8
    o.SlidingExpiration = true;
    o.Events.OnRedirectToLogin = ctx => ProblemResults.WriteAsync(ctx.HttpContext, 401, ProblemTypes.Unauthenticated);
    o.Events.OnRedirectToAccessDenied = ctx => ProblemResults.WriteAsync(ctx.HttpContext, 403, ProblemTypes.Forbidden);
    o.Events.OnValidatePrincipal = RushDayCookieEvents.ValidateAsync; // absolute lifetime, then SecurityStampValidator
});
services.Configure<SecurityStampValidatorOptions>(o => o.ValidationInterval = TimeSpan.FromMinutes(auth.SecurityStampIntervalMinutes)); // 5
services.AddDataProtection().SetApplicationName("RushDay").PersistKeysToDbContext<RushDayDbContext>();
services.AddAuthorization(o => o.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());
```

`RushDayCookieEvents.ValidateAsync` rejects the principal (`RejectPrincipal()` + `SignOutAsync`) when the `iat` claim is
older than `Auth:SessionAbsoluteHours` (12), then delegates to `SecurityStampValidator.ValidatePrincipalAsync`, which
re-reads the user every 5 minutes (one primary-key read per active session) and rejects when the security stamp
changed (password change, reset, lock, disable) or `disabled_at IS NOT NULL` (checked in `RushDayClaimsPrincipalFactory`
re-issue: a disabled user gets no principal). Password change calls `UpdateSecurityStampAsync` then `RefreshSignInAsync`
so the current session survives and every other session dies at its next validation.

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
| `demo` | `1` | when `is_demo` |
| `iat` | Unix seconds at sign-in | all |

Ownership checks read claims (`CurrentUser` accessor wraps them); route values are never trusted for identity.

### 2.3 Login, logout, password change, forced change

- `POST /api/auth/login`: `FindByNameAsync(username)`; when null or `disabled_at IS NOT NULL`, run
  `PasswordHasher.VerifyHashedPassword(DummyUser, DummyHash, password)` so timing matches, then answer 401. Otherwise
  `SignInManager.PasswordSignInAsync(user, password, isPersistent: false, lockoutOnFailure: true)`. Wrong password,
  locked out and unknown user all return the same 401 `invalid-credentials`; the body never distinguishes them. On
  the failure that trips lockout, audit `auth.locked_out`. On success: `last_login_at = now`, set `HttpContext.User`
  to the new principal, then `IAntiforgery.GetAndStoreTokens(HttpContext)` so the returned `csrfToken` is bound to
  the signed-in identity. Metrics `rushday.auth.logins{outcome=success|failed|locked_out}`.
- `POST /api/auth/logout`: `SignOutAsync(IdentityConstants.ApplicationScheme)` → 204. The SPA then fetches a fresh
  anonymous token from `/api/auth/csrf`.
- `POST /api/auth/change-password`: `ChangePasswordAsync` (400 `invalid-current-password` when the current password
  fails, 400 `weak-password` with `errors.newPassword[]` when policy fails), clear `must_change_password`,
  `RefreshSignInAsync`, audit `auth.password_changed` → 204.
- `MustChangePasswordFilter` (endpoint filter on the `/api` group): when the principal has `pwd_change=1` and the
  route is not one of `GET /api/auth/me`, `POST /api/auth/logout`, `POST /api/auth/change-password`,
  `GET /api/auth/csrf`, answer 403 `password-change-required`.
- No self-service reset (no email channel). Administrators reset via `POST /api/admin/accounts/{id}/reset-password`.
- `POST /api/auth/register` does not exist and `MapIdentityApi` is never called; the SPA fallback does not apply to
  `/api/*`, so the route answers 404 `not-found` as ProblemDetails.

## 3. Antiforgery

```csharp
services.AddAntiforgery(o =>
{
    o.HeaderName = "X-CSRF-TOKEN";
    o.Cookie.Name = "rushday.csrf";
    o.Cookie.HttpOnly = true;
    o.Cookie.SameSite = SameSiteMode.Strict;
    o.Cookie.SecurePolicy = /* same as the auth cookie */;
    o.SuppressXFrameOptionsHeader = true; // SecurityHeadersMiddleware sets it
});
```

- `AntiforgeryEndpointFilter` on the whole `/api` group calls `IAntiforgery.ValidateRequestAsync` for POST, PUT,
  PATCH and DELETE. Failure → 400 `urn:rushday:antiforgery`. No exemptions: login is protected too (login CSRF).
- The request token is delivered only in JSON bodies: `GET /api/auth/csrf` (anonymous), `POST /api/auth/login` and
  `GET /api/auth/me` (`csrfToken` field). Tokens are bound to the principal, so the SPA refreshes after login and
  logout; on a 400 `antiforgery` it refreshes once and retries once.
- k6 does the same two calls (`04-performance-and-ops.md` section 8).

## 4. Authorization policies (`Auth/Policies.cs`)

| Policy | Requirement | Failure |
|---|---|---|
| `StudentOnly` | role `Student` and claim `student_id` present | 403 `forbidden` |
| `LecturerOnly` | role `Lecturer` and claim `lecturer_id` present | 403 `forbidden` |
| `AdminOnly` | role `Admin` | 403 `forbidden` |
| `Staff` | role `Lecturer` or `Admin` | 403 `forbidden` |
| `TeachesModule` | `TeachesModuleRequirement` evaluated by `TeachesModuleHandler` against route value `code`: `Admin` passes; `Lecturer` passes when `LecturerModuleCache.GetCodesAsync(lecturerId)` (HybridCache 60 s, invalidated by `PUT /api/admin/modules/{code}/lecturers`) contains the code. Unknown code still evaluates the cache (empty set) so the answer is 403, never 404, for a non-member. | 403 `not-your-module` |

Structural rules that make cross-tenant reads impossible rather than merely forbidden:

- Student routes (`/api/me/*`) never take a student number; every query predicate uses `CurrentUser.StudentId`.
- Lecturer roster and marks queries join through `module_lecturers` (`WHERE ml.lecturer_id = @me AND m.code = @code`)
  so a policy bug yields an empty result, not another module's data.
- Only `/api/admin/*` accepts student numbers or account ids in the URL, and the group carries
  `.RequireAuthorization(Policies.AdminOnly)`.
- Fallback policy = authenticated; anonymous routes opt out with `.AllowAnonymous()`.
- An `Admin` cannot use student or lecturer routes (no `student_id`/`lecturer_id` claim); admin actions on students go
  through explicit override routes that are audited with `override: true`.

## 5. Rate limiting

`Microsoft.AspNetCore.RateLimiting` with numbers bound from `RateLimiting:*` so Render can tune them by environment
variable; Development values are relaxed so k6 can authenticate hundreds of students from one machine.

| Policy | Partition key | Limiter | Production | Development | Rejection |
|---|---|---|---|---|---|
| global (`/api` except `/api/health/*`) | single | Concurrency, `OldestFirst` | permit 24, queue 96 | permit 64, queue 256 | 503 `server-busy`, `Retry-After: 1`, metric `rushday.load_shed.rejected{policy=api}` |
| `login` (per user) | normalised username from the body, applied inside `AuthEndpoints.Login` via `LoginThrottle` (`PartitionedRateLimiter<string>`) | Sliding window 60 s, 6 segments | 10 | 100,000 | 429 `rate-limited`, `Retry-After` |
| `login` (per IP) | client IP after `ForwardedHeaders` | Sliding window 60 s, 6 segments | 600 | 100,000 | 429 `rate-limited` |
| `login` (CPU guard) | single | Concurrency | permit 8, queue 64 | permit 64, queue 512 | 429 `rate-limited`, `Retry-After: 2` |
| `enrol` (`POST/DELETE /api/me/enrolments*`) | `sub` claim | Token bucket | 5 tokens, +5 per 10 s | 1,000 | 429 `rate-limited` |
| `write` (every other mutation) | `sub` claim | Token bucket | 120 tokens, +120 per minute | 10,000 | 429 `rate-limited` |

Identity lockout (5 failures, 15 minutes) is the per-account brake; the per-username window is the spray brake; the
concurrency guard protects PBKDF2 CPU on 0.1 vCPU. Every 429 and 503 carries `Retry-After` in whole seconds and a
ProblemDetails body.

## 6. ProblemDetails

`AddProblemDetails(o => o.CustomizeProblemDetails = ctx => { ... })` sets `traceId` (`Activity.Current?.Id ??
HttpContext.TraceIdentifier`) on every problem and fills `type` when the framework produced the problem
(400 → `validation`, 401 → `unauthenticated`, 403 → `forbidden`, 404 → `not-found`, 405 → `method-not-allowed`,
413 → `payload-too-large`, 415 → `unsupported-media-type`, 429 → `rate-limited`, 500 → `internal-error`,
503 → `server-busy`). `UseExceptionHandler()` (no exception text), `UseStatusCodePages()`, `AddRequestTimeouts`
(15 s default → 503 `timeout`). Npgsql pool timeout (`NpgsqlException` with `IsTransient`) → 503 `server-busy`,
`Retry-After: 2`, metric `rushday.db.pool_wait_timeouts`.

Shape: `{ type: "urn:rushday:<slug>", title, status, detail, instance, traceId, ...extensions }`. `detail` is safe to
show to the caller. The slug catalogue is closed; adding one is a spec change:

| Status | Slug | Where |
|---|---|---|
| 400 | `validation`, `antiforgery`, `invalid-current-password`, `weak-password` | any / auth |
| 401 | `unauthenticated`, `invalid-credentials` | any / login |
| 403 | `forbidden`, `not-your-module`, `password-change-required` | any |
| 404 | `not-found`, `module-not-found`, `student-not-found`, `lecturer-not-found`, `account-not-found`, `window-not-found`, `publication-not-found`, `announcement-not-found`, `not-enrolled` | as named |
| 409 | `already-enrolled`, `module-full`, `enrolment-window-closed` (`opensAt`, `closesAt` extensions), `withdrawal-deadline-passed` (`withdrawalDeadlineAt`), `results-exist`, `module-locked`, `module-not-submitted`, `already-submitted`, `stale-mark` (`studentNumbers[]`), `nothing-to-publish`, `publication-live`, `username-taken`, `principal-has-account`, `module-code-taken`, `student-number-taken`, `staff-number-taken`, `window-exists` | as named |
| 422 | `credit-limit-exceeded` (`currentCredits`, `moduleCredits`, `limit`), `marks-incomplete` (`missing[]`), `not-enrolled-students` (`studentNumbers[]`), `capacity-below-enrolled` (`enrolledCount`), `publish-too-far-ahead`, `invalid-lecturer-assignment`, `window-dates-invalid`, `self-lockout`, `role-principal-mismatch` | as named |
| 429 | `rate-limited` | limiters |
| 500 | `internal-error` | unhandled |
| 503 | `server-busy`, `timeout` | shedding, pool, request timeout |

## 7. Shared shapes

```ts
Me { id: string; username: string; displayName: string; role: 'Student' | 'Lecturer' | 'Admin';
     studentNumber: string | null; staffNumber: string | null; mustChangePassword: boolean; isDemo: boolean; csrfToken: string }

TimetableEntry { moduleCode: string; day: 'monday'|'tuesday'|'wednesday'|'thursday'|'friday'|'saturday'|'sunday';
                 startTime: string; endTime: string; room: string }                       // "09:00"

WindowInfo { id: string; academicYear: string; semester: 'autumn' | 'spring'; opensAt: string; closesAt: string;
             withdrawalDeadlineAt: string; state: 'notYetOpen' | 'open' | 'closed' }

PublicationInfo { id: string; academicYear: string; semester: 'autumn' | 'spring'; publishAt: string;
                  state: 'scheduled' | 'live'; gradeCount: number; moduleCount: number; createdAt: string;
                  createdBy: string | null; note: string | null }

Lecturer { staffNumber: string; fullName: string; title: string; role: 'leader' | 'teacher' }

ModuleSummary { code: string; title: string; department: string; level: 1 | 2 | 3; credits: number;
                semester: 'autumn' | 'spring'; capacity: number; enrolledCount: number; placesRemaining: number;
                isActive: boolean; lecturers: Lecturer[];
                enrolmentState: 'notYetOpen' | 'open' | 'closed' | 'noWindow'; windowOpensAt: string | null;
                windowClosesAt: string | null; withdrawalDeadlineAt: string | null }

ModuleDetail = ModuleSummary & { description: string | null; timetable: TimetableEntry[] }

GradeResult { moduleCode: string; moduleTitle: string; credits: number; semester: 'autumn' | 'spring';
              mark: number; publishedAt: string }

AnnouncementView { id: string; scope: 'university' | 'module'; moduleCode: string | null; title: string; body: string;
                   pinned: boolean; publishedAt: string; expiresAt: string | null; author: string;
                   createdAt: string; updatedAt: string }

MarksStatus { status: 'draft' | 'submitted' | 'published'; entered: number; missing: number;
              submittedAt: string | null; publishedAt: string | null }

AuditEventView { id: string; occurredAt: string; actorUsername: string | null; actorRole: string | null; action: string;
                 subjectType: string; subjectId: string | null; studentNumber: string | null; moduleCode: string | null;
                 details: Record<string, unknown> | null; requestId: string | null }

AccountView { id: string; username: string; displayName: string; role: 'Student' | 'Lecturer' | 'Admin';
              studentNumber: string | null; staffNumber: string | null; email: string | null;
              state: 'active' | 'locked' | 'disabled'; lockoutEnd: string | null; mustChangePassword: boolean;
              isDemo: boolean; createdAt: string; lastLoginAt: string | null }

Paged<T> { items: T[]; page: number; pageSize: number; total: number }
```

`enrolmentState` and window instants on `ModuleSummary` come from the cached windows for the current academic year and
the module's semester. `MarksStatus.status` is module-wide: `published` if any grade on the module is Published,
else `submitted` if any is Submitted, else `draft`; `entered` counts grades for active enrolments, `missing` counts
active enrolments without a grade.

## 8. Route table

### 8.1 Public (`.AllowAnonymous()`, no rate limit other than global)

| Method and route | Response | Codes |
|---|---|---|
| `GET /api` | `{ name: "RushDay", story: <the story sentence pair>, commit: string, environment: string, links: { health: "/api/health/live", ready: "/api/health/ready", status: "/api/public/status", openapi: "/api/openapi/v1.json", login: "/api/auth/login", github: "https://github.com/Ahmeddayyan/RushDay" } }` | 200 |
| `GET /api/health/live` | `{ status: "Healthy" }` (no dependencies) | 200 |
| `GET /api/health/ready` | `{ status: "Healthy" \| "Unhealthy", checks: [{ name: "database", status, durationMs }] }` (`AddDbContextCheck`) | 200, 503 |
| `GET /api/public/status` | `{ serverTime, institution: { name, shortName, timeZone }, academicYear, nextPublication: { academicYear, semester, publishAt } \| null, enrolmentWindows: WindowInfo[], demo: { accounts: [{ role, username, password, hint }] } \| null }`; output-cached 10 s; `demo` is null unless `Demo:Enabled` | 200 |
| `GET /api/openapi/v1.json` | OpenAPI document (`MapOpenApi("/api/openapi/{documentName}.json")`) | 200 |
| `GET /api/auth/csrf` | `{ csrfToken }`; sets `rushday.csrf` | 200 |
| `POST /api/auth/login` (`login` limiters) | body `{ username: string (1..64), password: string (1..128) }` → `Me`; sets `rushday.auth` | 200; 401 `invalid-credentials`; 429 |

### 8.2 Authenticated, any role

| Method and route | Request | Response | Codes |
|---|---|---|---|
| `GET /api/auth/me` | | `Me` (fresh `csrfToken`) | 200 |
| `POST /api/auth/logout` | | | 204 |
| `POST /api/auth/change-password` | `{ currentPassword, newPassword }` | | 204; 400 `invalid-current-password`; 400 `weak-password` |
| `GET /api/modules` | | `ModuleSummary[]` — every `is_active` module ordered by code; viewer-agnostic; served from `catalogue:all` (30 s) | 200 |
| `GET /api/modules/{code}` | | `ModuleDetail` — the module row is read uncached so `enrolledCount` is live | 200; 404 `module-not-found` |
| `GET /api/announcements` | | `AnnouncementView[]` visible now (`published_at <= now`, not expired, not deleted), filtered: Student → university + modules with an active enrolment; Lecturer → university + assigned modules; Admin → all; pinned first, then `publishedAt` desc; max 50 | 200 |

### 8.3 Student (`StudentOnly`; group `/api/me`)

| Method and route | Request | Response | Codes |
|---|---|---|---|
| `GET /api/me/dashboard` | | `DashboardResponse` (below) | 200 |
| `GET /api/me/results` | | `{ semesters: [{ semester, state: 'published' \| 'scheduled' \| 'pending', publishAt: string \| null, results: GradeResult[] }], weightedAverage: number \| null, classification: string \| null }` | 200 |
| `GET /api/me/timetable` | | `TimetableEntry[]` for active enrolments | 200 |
| `GET /api/me/enrolments` | | `[{ moduleCode, title, credits, semester, status: 'active' \| 'withdrawn', enrolledAt, withdrawnAt: string \| null, canWithdraw: boolean, withdrawalDeadlineAt: string \| null }]` | 200 |
| `POST /api/me/enrolments` (`enrol`) | `{ moduleCode }` | `{ moduleCode, enrolledAt, placesRemaining }`, `Location: /api/me/enrolments/{code}` | 201; 404 `module-not-found`; 409 `already-enrolled`, `module-full`, `enrolment-window-closed`; 422 `credit-limit-exceeded` |
| `DELETE /api/me/enrolments/{code}` (`enrol`) | | | 204; 404 `not-enrolled`; 409 `withdrawal-deadline-passed`, `results-exist` |

```ts
DashboardResponse {
  studentNumber: string; fullName: string; programme: string; yearOfStudy: number; academicYear: string;
  modules: [{ code; title; credits; semester; enrolledAt; canWithdraw: boolean; withdrawalDeadlineAt: string | null }];
  timetable: TimetableEntry[];                      // whole week; the SPA derives "today"
  results: GradeResult[];                           // visible grades only
  weightedAverage: number | null; classification: string | null;
  credits: { autumn: number; spring: number; limit: 60 };
  nextPublication: { academicYear; semester; publishAt } | null;   // earliest scheduled publication in the future
  enrolmentWindows: WindowInfo[];                   // current academic year
  announcements: AnnouncementView[];                // latest 5 visible, pinned first
}
```

`results.semesters[].state`: `published` when at least one visible grade exists for that semester; else `scheduled`
when the student has grades on that semester's modules with `status = Published AND published_at > now` (`publishAt`
= the earliest such instant; the mark itself is never sent); else `pending` when the student has an active enrolment in
that semester. Semesters with neither are omitted. `weightedAverage` and `classification` use visible grades only
(`Classification.WeightedAverage`, `Classification.FromAverage`).

### 8.4 Lecturer (`LecturerOnly`; group `/api/lecturer`; `TeachesModule` wherever `{code}` appears; `write` limiter on mutations)

| Method and route | Request | Response | Codes |
|---|---|---|---|
| `GET /api/lecturer/modules` | | `[ModuleSummary & { myRole: 'leader' \| 'teacher'; marks: MarksStatus }]` | 200 |
| `GET /api/lecturer/modules/{code}/roster` | `q?`, `page=1`, `pageSize=50` (≤200) | `{ module: ModuleSummary; items: [{ studentNumber; fullName; programme; yearOfStudy; status: 'active' \| 'withdrawn'; enrolledAt; withdrawnAt }]; page; pageSize; total }` active first, then by student number | 200; 403 `not-your-module`; 404 `module-not-found` |
| `GET /api/lecturer/modules/{code}/marks` | | `MarksSheet` (below) | 200; 403; 404 |
| `PUT /api/lecturer/modules/{code}/marks` | `{ rows: [{ studentNumber; mark: number (0..100 integer); version: number \| null }] }` (1..500 rows) | `MarksSheet` | 200; 403; 404; 409 `module-locked`, `stale-mark`; 422 `not-enrolled-students` |
| `POST /api/lecturer/modules/{code}/marks/submit` | | `{ code; status: 'submitted'; submittedAt; gradeCount }` | 200; 403; 404; 409 `already-submitted`, `module-locked` (published); 422 `marks-incomplete` |
| `GET /api/lecturer/modules/{code}/announcements` | | `AnnouncementView[]` (module scope, including future and expired, not deleted) | 200; 403; 404 |
| `POST /api/lecturer/modules/{code}/announcements` | `{ title (1..120); body (1..4000); pinned?: boolean; publishedAt?: string; expiresAt?: string }` | `AnnouncementView` | 201; 403; 404 |
| `PUT /api/lecturer/modules/{code}/announcements/{id}` | same body | `AnnouncementView` | 200; 403; 404 `announcement-not-found` |
| `DELETE /api/lecturer/modules/{code}/announcements/{id}` | | | 204; 403; 404 |
| Should: `GET /api/lecturer/modules/{code}/roster.csv` | | `text/csv` attachment `roster-{code}.csv` | 200 |

```ts
MarksSheet {
  code: string; title: string; status: 'draft' | 'submitted' | 'published'; submittedAt: string | null; publishedAt: string | null;
  rows: [{ studentNumber: string; fullName: string; enrolmentStatus: 'active' | 'withdrawn';
           mark: number | null; version: number | null; updatedAt: string | null; enteredBy: string | null }]
}
```

`PUT marks` rules (`MarksService.SaveAsync`, one transaction, all-or-nothing): module status must be `draft`
(else 409 `module-locked`); every `studentNumber` must have an **active** enrolment (else 422 `not-enrolled-students`);
for existing grade rows `version` must equal the stored version (else 409 `stale-mark`, nothing saved); new rows are
created as `Draft` with `version = 1`; changed rows get `version + 1`, `updated_at`, `entered_by_user_id`; an unchanged
mark is not written; every insert audits `grade.entered` and every change `grade.changed` with `{ before, after }`.
Metric `rushday.grades.saved` += rows written. `Admin` may call these routes too (policy `TeachesModule` passes).

`submit` rules: every active enrolment on the module must have a Draft grade (else 422 `marks-incomplete` with
`missing: string[]`); sets every Draft grade to `Submitted` with `submitted_at`; audit `module.marks_submitted`
with `{ gradeCount }`. Withdrawn students' draft grades are submitted too (history) and remain invisible until published.

### 8.5 Administrator (`AdminOnly`; group `/api/admin`; `write` limiter on mutations)

**Overview and settings**

| Method and route | Request | Response | Codes |
|---|---|---|---|
| `GET /api/admin/overview` | | `{ counts: { students; lecturers; modules; activeEnrolments; accounts; lockedAccounts; disabledAccounts }; enrolmentWindows: WindowInfo[]; nextPublication: PublicationInfo \| null; submissionProgress: [{ semester; modulesTotal; draft; submitted; published }]; recentAudit: AuditEventView[] (10); database: 'ok' \| 'degraded' }` | 200 |
| `GET /api/admin/settings` | | `{ academicYear; institutionName; institutionShortName; timeZone; updatedAt }` | 200 |
| `PUT /api/admin/settings` | `{ academicYear: /^\d{4}\/\d{2}$/; institutionName (1..200); institutionShortName (1..32); timeZone: valid IANA id }` | same as GET | 200; audit `settings.changed` |

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
| `GET /api/admin/results` | `semester` (required), `academicYear?` (defaults to settings) | `{ semester; modules: [{ code; title; leader: string \| null; enrolledCount; marks: MarksStatus }]; publications: PublicationInfo[] }` | 200 |
| `POST /api/admin/results/publish` | `{ academicYear; semester; publishAt }` | `{ publication: PublicationInfo; published: { modules: number; grades: number }; excluded: [{ code; status: 'draft'; marksMissing: number }] }` | 200; 409 `nothing-to-publish`; 422 `publish-too-far-ahead` (> 90 days ahead) |
| `PUT /api/admin/results/publications/{id}` | `{ publishAt }` | `PublicationInfo` | 200; 404 `publication-not-found`; 409 `publication-live`; 422 `publish-too-far-ahead` |
| `POST /api/admin/results/modules/{code}/return-to-draft` | `{ reason (10..400) }` | `{ code; status: 'draft' }` | 200; 404; 409 `module-not-submitted` (status draft), `module-locked` (status published) |

`publish` rules (`ResultsPublicationService.PublishAsync`, one transaction): `publishAt` earlier than now is
replaced by now; insert `results_publications`; `UPDATE grades g SET status = 'Published', published_at = @publishAt,
publication_id = @id, updated_at = now() FROM modules m WHERE g.module_id = m.id AND m.semester = @semester AND
g.status = 'Submitted'`; `excluded` lists modules of the semester with any Draft grade or missing marks; audit
`results.published` with counts; metric `rushday.results.published`; invalidate `publication:next`. Partially entered
marks are never published and there is no flag to include drafts. Calling publish again for the same semester
publishes newly submitted modules under a new publication row. Reschedule (`PUT publications/{id}`) updates the
publication row and `UPDATE grades SET published_at = @new WHERE publication_id = @id`, only while
`publish_at > now`.

**Students**

| Method and route | Request | Response | Codes |
|---|---|---|---|
| `GET /api/admin/students` | `q?` (number or name prefix, case-insensitive), `page`, `pageSize` | `Paged<{ studentNumber; fullName; programme; yearOfStudy; email; accountState: 'none' \| 'active' \| 'locked' \| 'disabled' }>` | 200 |
| `POST /api/admin/students` | `{ studentNumber: /^S\d{6}$/; fullName (1..200); programme (1..200); yearOfStudy (1..6); email? }` | the row | 201; 409 `student-number-taken` |
| `GET /api/admin/students/{studentNumber}` | | `AdminStudentView` (below) | 200; 404 `student-not-found` |
| `POST /api/admin/students/{studentNumber}/enrolments` | `{ moduleCode; reason (10..400); forceCapacity?: boolean }` | `{ moduleCode; enrolledAt; placesRemaining; capacityRaised: boolean }` | 201; 404 `student-not-found`, `module-not-found`; 409 `already-enrolled`, `module-full` (without `forceCapacity`) |
| `POST /api/admin/students/{studentNumber}/enrolments/{code}/withdraw` | `{ reason (10..400) }` | | 204; 404 `not-enrolled` |

Override rules: windows and the credit limit are ignored; capacity applies unless `forceCapacity`, which runs
`UPDATE modules SET capacity = capacity + 1, enrolled_count = enrolled_count + 1 WHERE id = @m` and audits both
`module.updated` (`{ capacity: { before, after }, reason }`) and `enrolment.admin_created` (`{ reason, override: true,
forceCapacity: true }`). Admin withdrawal ignores the withdrawal deadline and the `results-exist` rule and audits
`enrolment.admin_withdrawn` with the reason.

```ts
AdminStudentView {
  student: { studentNumber; fullName; programme; yearOfStudy; email };
  account: AccountView | null;
  enrolments: [{ moduleCode; title; credits; semester; status; source: 'seed' | 'self' | 'admin'; enrolledAt; withdrawnAt }];
  grades: [{ moduleCode; moduleTitle; credits; semester; mark; status: 'draft' | 'submitted' | 'published'; publishedAt: string | null; visibleToStudent: boolean }];
  weightedAverage: number | null; classification: string | null;   // visible grades only, as the student sees it
  recentAudit: AuditEventView[];                                    // 10
}
```

**Modules and lecturers**

| Method and route | Request | Response | Codes |
|---|---|---|---|
| `GET /api/admin/modules` | `includeInactive?=false` | `[ModuleSummary & { description: string \| null; marks: MarksStatus }]` | 200 |
| `POST /api/admin/modules` | `{ code: /^[A-Z]{2}\d{4}$/; title (1..200); description? (≤2000); credits (5..60); capacity (0..10000); semester }` | `ModuleDetail` | 201; 409 `module-code-taken` |
| `PUT /api/admin/modules/{code}` | `{ title; description; credits; capacity; semester; isActive }` | `ModuleDetail` | 200; 404; 422 `capacity-below-enrolled` |
| `PUT /api/admin/modules/{code}/lecturers` | `{ assignments: [{ staffNumber; role: 'leader' \| 'teacher' }] }` (exactly one leader, no duplicates) | `ModuleDetail` | 200; 404 `module-not-found`, `lecturer-not-found`; 422 `invalid-lecturer-assignment` |
| `GET /api/admin/lecturers` | `q?` | `[{ staffNumber; fullName; title; department; email; hasAccount: boolean; moduleCodes: string[] }]` | 200 |
| `POST /api/admin/lecturers` | `{ staffNumber: /^L\d{5}$/; fullName; title (Dr\|Prof\|Mr\|Ms\|Mx); department (1..8); email? }` | the row | 201; 409 `staff-number-taken` |

Module mutations invalidate `catalogue:all` and audit `module.created|updated|lecturers_set`; changing `credits` on a
module with active enrolments is allowed and audited (the UI warns). Lecturer assignment invalidates
`lecturer-modules:{lecturerId}` for every lecturer added or removed.

**Accounts**

| Method and route | Request | Response | Codes |
|---|---|---|---|
| `GET /api/admin/accounts` | `q?` (username or display name), `role?`, `state?` (`active`\|`locked`\|`disabled`), `page`, `pageSize` | `Paged<AccountView>` | 200 |
| `POST /api/admin/accounts` | `{ username (1..64, allowed chars); displayName (1..200); role; studentNumber?; staffNumber?; email?; temporaryPassword? (policy) }` | `{ account: AccountView; temporaryPassword: string }` (generated 16-char password when omitted; shown once) | 201; 404 `student-not-found`, `lecturer-not-found`; 409 `username-taken`, `principal-has-account`; 422 `role-principal-mismatch` |
| `POST /api/admin/accounts/{id}/lock` | | `AccountView` (`lockoutEnd = 9999-12-31T00:00:00Z`) | 200; 404 `account-not-found`; 422 `self-lockout` |
| `POST /api/admin/accounts/{id}/unlock` | | `AccountView` | 200; 404 |
| `POST /api/admin/accounts/{id}/disable` | | `AccountView` | 200; 404; 422 `self-lockout` |
| `POST /api/admin/accounts/{id}/enable` | | `AccountView` | 200; 404 |
| `POST /api/admin/accounts/{id}/reset-password` | `{ temporaryPassword? }` | `{ temporaryPassword }` | 200; 404; 422 `self-lockout` is not raised (admins may reset their own) |

Provision, reset, lock and disable set `must_change_password = true` (reset and provision), update the security
stamp (lock, disable, reset) and audit `account.provisioned|locked|unlocked|disabled|enabled|password_reset`. A
`Student` role requires `studentNumber` and a `Lecturer` role requires `staffNumber`; an `Admin` requires neither.

**Announcements (university scope)**

| Method and route | Request | Response | Codes |
|---|---|---|---|
| `GET /api/admin/announcements` | | `AnnouncementView[]` (all scopes, including future and expired, not deleted) | 200 |
| `POST /api/admin/announcements` | `{ title; body; pinned?; publishedAt?; expiresAt? }` | `AnnouncementView` | 201 |
| `PUT /api/admin/announcements/{id}` | same | `AnnouncementView` | 200; 404 |
| `DELETE /api/admin/announcements/{id}` | | | 204; 404 |

Admins may edit or delete announcements of any scope through these routes. Mutations invalidate
`announcements:university` and audit `announcement.created|updated|deleted`.

**Audit and ops**

| Method and route | Request | Response | Codes |
|---|---|---|---|
| `GET /api/admin/audit` | `actor?` (username), `studentNumber?`, `moduleCode?`, `action?`, `from?`, `to?`, `page`, `pageSize` (≤200) | `Paged<AuditEventView>` newest first | 200 |
| `GET /api/admin/audit/export.csv` | same filters | `text/csv; charset=utf-8`, attachment `audit-<from>-<to>.csv`, at most 50,000 rows, columns `occurredAt,actorUsername,actorRole,action,subjectType,subjectId,studentNumber,moduleCode,details,requestId` | 200 |
| `GET /api/admin/ops/metrics` | | `OpsSnapshot` (`04-performance-and-ops.md` section 6) | 200 |
| `POST /api/admin/ops/reconcile` | | `{ modulesCorrected: [{ code; before; after }] }` (runs backfill step 1; audits `ops.reconciled`) | 200 |

### 8.6 Removed routes

`GET /students/{n}/dashboard`, `POST /students/{n}/enrolments`, `GET /modules`, `GET /modules/{code}`, `GET /health`,
`GET /openapi/v1.json` and the interim `wwwroot/index.html`. `src/RushDay.Api/RushDay.Api.http` is rewritten for the
new routes (csrf → login → me → dashboard → enrol).
