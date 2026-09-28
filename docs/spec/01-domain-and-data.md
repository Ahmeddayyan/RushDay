# RushDay v1 specification: 01. Domain and data

Scope: entities, tables, columns, constraints, indexes, the single EF migration, the idempotent startup backfills
that run against the already-populated Neon database, and the demo accounts. Decisions D1–D3, D8–D11, D17,
D22 and D24 of `00-overview.md` apply.

What exists today (migration `20260927150750_InitialCreate`): `students`, `modules`, `timetable_slots`, `enrolments`,
`grades`, all `uuid` keyed, snake_case, seeded deterministically (`DatabaseSeeder`, `Random(42)`): 20,000 students,
121 modules (60 autumn with capacity 1,500, 60 spring with capacity 300, plus `CS3099` "Advanced Machine Learning",
spring, capacity 30), 80,000 autumn enrolments (4 per student, 15 credits each = 60 autumn credits), 80,000 grades all
`published_at = 2026-09-28T09:00:00Z`. The local database additionally holds 154 enrolments on CS3099 from the v0
load run (oversold by 124); the Neon database may hold a similar number from demo visitors. Nothing is re-seeded.

## 1. Conventions

- Identifiers snake_case via `UseSnakeCaseNamingConvention()`; new Identity tables renamed with `ToTable`.
- Primary keys `uuid` from `Guid.CreateVersion7()` (time-ordered) unless stated; instants `timestamp with time zone`
  (`DateTimeOffset` in C#); enum-like columns `varchar` holding the PascalCase C# enum name (EF `HasConversion<string>()`)
  with a CHECK constraint; `modules.semester` stays `integer` (1 Autumn, 2 Spring).
- Every new NOT NULL column on an existing table is added with a migration-time default so the populated database
  migrates in one statement; the default is then dropped unless the table below says "default kept".
- Foreign keys to `users` are `ON DELETE RESTRICT` (users are never deleted; they are disabled). Foreign keys from
  child rows to `modules`/`students` keep the existing `CASCADE`.
- Domain entities live in `src/RushDay.Domain`; `ApplicationUser` lives in `src/RushDay.Infrastructure/Identity`
  because the login store is a persistence concern.

## 2. Identity tables (ASP.NET Core Identity, EF Core store)

`RushDayDbContext : IdentityDbContext<ApplicationUser, IdentityRole<Guid>, Guid>, IDataProtectionKeyContext`.
Standard Identity columns are listed once for `users`; the other tables keep Identity's default shape, renamed.

### `users` (`ApplicationUser : IdentityUser<Guid>`)

| Column | Type | Constraints / notes |
|---|---|---|
| id | uuid | PK |
| user_name | varchar(256) | login identifier exactly as provisioned: `S000001`, `L00001`, `admin` |
| normalized_user_name | varchar(256) | unique index (Identity default); login is case-insensitive |
| email, normalized_email | varchar(256) null | optional; non-unique index (Identity default) |
| email_confirmed, phone_number, phone_number_confirmed, two_factor_enabled | Identity defaults | unused in v1, kept |
| password_hash, security_stamp, concurrency_stamp | text null | Identity-managed |
| lockout_end | timestamptz null | Identity lockout; admin "lock" sets it to `9999-12-31` |
| lockout_enabled | boolean | true for every user |
| access_failed_count | integer | Identity |
| **display_name** | varchar(200) not null | shown in the top bar and audit log |
| **student_id** | uuid null | FK `students(id)` RESTRICT; unique index `ix_users_student_id` |
| **lecturer_id** | uuid null | FK `lecturers(id)` RESTRICT; unique index `ix_users_lecturer_id` |
| **must_change_password** | boolean not null default false | provisioned and reset accounts start true; demo accounts false |
| **is_demo** | boolean not null default false | created by the demo backfill; shown as a banner on the accounts page |
| **created_at** | timestamptz not null | |
| **disabled_at** | timestamptz null | disabled users cannot sign in; existing sessions die at the next security-stamp check |
| **last_login_at** | timestamptz null | updated on successful login |
| CHECK `ck_users_one_principal` | | `NOT (student_id IS NOT NULL AND lecturer_id IS NOT NULL)` |

Rule enforced in `AccountService` (Identity's join table cannot carry it) and asserted by an integration test:
a `Student`-role user has `student_id`; a `Lecturer`-role user has `lecturer_id`; an `Admin` has neither; every user
has exactly one role.

### Other Identity tables

| Table | Shape |
|---|---|
| `roles` | `id uuid PK`, `name varchar(256)`, `normalized_name varchar(256)` unique, `concurrency_stamp text`. Rows: `Student`, `Lecturer`, `Admin` with fixed ids `00000000-0000-0000-0000-000000000001/2/3` (`RushDayRoles`). |
| `user_roles` | `user_id uuid`, `role_id uuid`, PK (user_id, role_id), FKs cascade |
| `user_claims` | `id integer identity PK`, `user_id`, `claim_type`, `claim_value` (unused in v1; claims are computed at sign-in) |
| `role_claims` | `id integer identity PK`, `role_id`, `claim_type`, `claim_value` (unused) |
| `user_logins` | `login_provider`, `provider_key`, `provider_display_name`, `user_id`; PK (login_provider, provider_key) (reserved for Entra ID, Should) |
| `user_tokens` | `user_id`, `login_provider`, `name`, `value`; PK (user_id, login_provider, name) (unused) |
| `data_protection_keys` | `id integer identity PK`, `friendly_name text`, `xml text` (from `Microsoft.AspNetCore.DataProtection.EntityFrameworkCore`), so cookies survive redeploys |

## 3. Domain tables

### `students` (existing; changed)

Add `email varchar(256) null`. Everything else unchanged (`student_number varchar(16)` unique, `full_name`,
`programme`, `year_of_study`).

### `lecturers` (new)

| Column | Type | Constraints |
|---|---|---|
| id | uuid | PK |
| staff_number | varchar(16) not null | unique `ix_lecturers_staff_number`; format `L#####` |
| full_name | varchar(200) not null | |
| title | varchar(16) not null | `Dr`, `Prof` |
| department | varchar(8) not null | `CS`, `MA`, `PH`, `EE` |
| email | varchar(256) null | |

### `module_lecturers` (new)

| Column | Type | Constraints |
|---|---|---|
| module_id | uuid | FK `modules(id)` CASCADE |
| lecturer_id | uuid | FK `lecturers(id)` CASCADE |
| role | varchar(16) not null | CHECK `ck_module_lecturers_role` in (`Leader`, `Teacher`) |
| assigned_at | timestamptz not null | |
| assigned_by_user_id | uuid null | FK `users(id)` RESTRICT; null = seed |
| PK (module_id, lecturer_id); index `ix_module_lecturers_lecturer_id` | | exactly one `Leader` per module is a service rule |

### `modules` (existing; changed)

| Added column | Type | Migration handling |
|---|---|---|
| department | varchar(8) not null | add with default `''`, `UPDATE modules SET department = left(code, 2)`, drop default |
| description | varchar(2000) null | |
| enrolled_count | integer not null | default `0` **kept** (model `HasDefaultValue(0)`), then reconciled from active enrolments (section 6 step 1 SQL runs inside the migration too) |
| is_active | boolean not null | default `true` kept |
| updated_at | timestamptz null | |
| CHECK `ck_modules_enrolled_count_non_negative` | | `enrolled_count >= 0`. There is deliberately no `<= capacity` CHECK (D11). |
| CHECK `ck_modules_capacity_positive` | | `capacity >= 0` |

Existing: `code varchar(16)` unique, `title varchar(200)`, `credits`, `capacity`, `semester integer`.

### `enrolments` (existing; changed)

| Added column | Type | Migration handling |
|---|---|---|
| status | varchar(16) not null | CHECK `ck_enrolments_status` in (`Active`, `Withdrawn`); default `'Active'` kept (model `HasDefaultValue`) |
| source | varchar(16) not null | CHECK `ck_enrolments_source` in (`Seed`, `Self`, `Admin`); add with default `'Seed'`, drop default |
| withdrawn_at | timestamptz null | |
| created_by_user_id | uuid null | FK `users(id)` RESTRICT; null for seed and for self-enrolments (the student is the actor; the audit row names them) |
| updated_at | timestamptz null | |
| Index `ix_enrolments_module_id_status` (module_id, status) | | replaces `ix_enrolments_module_id` (dropped) |

Kept: unique `ix_enrolments_student_id_module_id` (the row toggles status instead of being deleted, D10), `enrolled_at`.

### `grades` (existing; changed)

| Column | Type | Migration handling |
|---|---|---|
| status | varchar(16) not null | CHECK `ck_grades_status` in (`Draft`, `Submitted`, `Published`); add with default `'Published'` (every existing row is a published mark), drop default |
| published_at | timestamptz **null** | `AlterColumn` to nullable; existing values kept |
| publication_id | uuid null | FK `results_publications(id)` RESTRICT; set by backfill step 5 for existing rows |
| entered_by_user_id | uuid null | FK `users(id)` RESTRICT |
| submitted_at | timestamptz null | |
| updated_at | timestamptz not null | add with `defaultValueSql: "now()"`, drop default |
| version | integer not null | default `1` kept; incremented on every mark change (optimistic concurrency for the marks grid) |
| CHECK `ck_grades_mark_range` | | `mark >= 0 AND mark <= 100` |
| CHECK `ck_grades_published_has_instant` | | `status <> 'Published' OR published_at IS NOT NULL` |
| Index `ix_grades_student_id_status_published_at` (student_id, status, published_at) | | the results-day read path |
| Index `ix_grades_module_id_status` (module_id, status) | | replaces `ix_grades_module_id` (dropped) |

Kept: unique `ix_grades_student_id_module_id`, `mark integer`.

### `academic_settings` (new, singleton)

| Column | Type | Constraints |
|---|---|---|
| id | integer | PK, CHECK `ck_academic_settings_singleton` (`id = 1`) |
| academic_year | varchar(9) not null | `2026/27` |
| institution_name | varchar(200) not null | `RushDay Demo University` |
| institution_short_name | varchar(32) not null | `RushDay` |
| time_zone | varchar(64) not null | IANA id, `Europe/London` |
| updated_at | timestamptz not null | |
| updated_by_user_id | uuid null | FK `users(id)` RESTRICT |

### `enrolment_windows` (new)

| Column | Type | Constraints |
|---|---|---|
| id | uuid | PK |
| academic_year | varchar(9) not null | |
| semester | integer not null | CHECK `ck_enrolment_windows_semester` in (1, 2) |
| opens_at, closes_at, withdrawal_deadline_at | timestamptz not null | CHECK `ck_enrolment_windows_order`: `opens_at < closes_at AND closes_at <= withdrawal_deadline_at` |
| created_by_user_id | uuid null | FK `users(id)` RESTRICT |
| updated_at | timestamptz not null | |
| Unique `ix_enrolment_windows_academic_year_semester` | | |

### `results_publications` (new)

| Column | Type | Constraints |
|---|---|---|
| id | uuid | PK |
| academic_year | varchar(9) not null | |
| semester | integer not null | CHECK in (1, 2) |
| publish_at | timestamptz not null | the instant grades become visible; may be rescheduled while `publish_at > now` |
| created_at | timestamptz not null | |
| created_by_user_id | uuid null | FK `users(id)` RESTRICT; null = seed |
| grade_count, module_count | integer not null | counts at publication time |
| note | varchar(400) null | |
| Index `ix_results_publications_semester_created_at` (academic_year, semester, created_at DESC) | | |

`state` is derived by reads: `scheduled` when `publish_at > now`, else `live`.

### `announcements` (new)

| Column | Type | Constraints |
|---|---|---|
| id | uuid | PK |
| scope | varchar(16) not null | CHECK in (`University`, `Module`) |
| module_id | uuid null | FK `modules(id)` CASCADE; CHECK `ck_announcements_scope_module`: `(scope = 'Module') = (module_id IS NOT NULL)` |
| title | varchar(120) not null | |
| body | varchar(4000) not null | plain text; rendered with line breaks only |
| pinned | boolean not null default false | default kept |
| published_at | timestamptz not null | may be future |
| expires_at | timestamptz null | |
| created_by_user_id | uuid not null | FK `users(id)` RESTRICT |
| created_at, updated_at | timestamptz not null | |
| deleted_at | timestamptz null | soft delete; deleted rows are excluded from every read except the audit log |
| Indexes | | `(scope, published_at DESC)`, `(module_id, published_at DESC)` |

### `audit_events` (new, append-only)

| Column | Type | Notes |
|---|---|---|
| id | uuid | PK, v7 (time-ordered) |
| occurred_at | timestamptz not null | |
| actor_user_id | uuid null | null = system/startup |
| actor_username | varchar(64) null | denormalised for display |
| actor_role | varchar(16) null | |
| action | varchar(64) not null | one of `AuditActions` (`03-security.md` section 7) |
| subject_type | varchar(32) not null | `Enrolment`, `Grade`, `Module`, `Publication`, `Announcement`, `Account`, `Settings`, `Window`, `System` |
| subject_id | varchar(64) null | uuid, module code or student number as text |
| student_id, module_id | uuid null | denormalised for filtering |
| details | jsonb null | before/after values, reason, decision; serialised by `AuditWriter` |
| request_id | varchar(64) null | `Activity.Current?.Id ?? HttpContext.TraceIdentifier` |
| ip_hash | varchar(64) null | `sha256(clientIp + dailySalt)` hex, first 32 chars |
| Indexes | | `(occurred_at DESC)`, `(student_id, occurred_at DESC)`, `(module_id, occurred_at DESC)`, `(actor_user_id, occurred_at DESC)`, `(action, occurred_at DESC)` |

The application never issues UPDATE or DELETE against this table; there is no EF entity method that would.

### `data_backfills` (new)

| Column | Type |
|---|---|
| name | varchar(64) PK |
| completed_at | timestamptz not null |
| rows_affected | integer not null |
| notes | varchar(400) null |

Shown on the admin ops page.

## 4. Domain model changes (`src/RushDay.Domain`)

Lifecycle properties become settable (`{ get; set; }`); identity and natural-key properties stay `init`.

| File | Change |
|---|---|
| `Students/Student.cs` | add `string? Email` |
| `Lecturers/Lecturer.cs` | new: `Id`, `StaffNumber`, `FullName`, `Title`, `Department`, `Email?` |
| `Modules/Module.cs` | add `Department`, `Description?`, `EnrolledCount` (set), `IsActive` (set), `UpdatedAt?` (set), computed `Level => Code[2] - '0'`; `Title`, `Credits`, `Capacity`, `Semester` become settable |
| `Modules/ModuleLecturer.cs`, `Modules/ModuleLecturerRole.cs` | new: `ModuleId`, `LecturerId`, `Role` (`Leader`, `Teacher`), `AssignedAt`, `AssignedByUserId?` |
| `Enrolments/Enrolment.cs` | add `Status` (`EnrolmentStatus.Active/Withdrawn`), `Source` (`EnrolmentSource.Seed/Self/Admin`), `WithdrawnAt?`, `CreatedByUserId?`, `UpdatedAt?`; `EnrolledAt` settable (reactivation) |
| `Enrolments/EnrolmentRules.cs` | `Evaluate(Module module, int currentEnrolledCount, int studentCreditsInSemester, bool alreadyEnrolled, bool windowOpen)`; order: `AlreadyEnrolled`, `WindowClosed`, `CreditLimitExceeded`, `ModuleFull`, `Accepted`. `MaxCreditsPerSemester = 60` unchanged. |
| `Enrolments/EnrolmentDecision.cs` | add `WindowClosed` |
| `Enrolments/EnrolmentWindow.cs` | new entity with pure `bool IsOpenAt(DateTimeOffset now)` and `bool AllowsWithdrawalAt(DateTimeOffset now)` |
| `Grades/Grade.cs` | `PublishedAt` becomes `DateTimeOffset?`; add `Status` (`GradeStatus.Draft/Submitted/Published`), `PublicationId?`, `EnteredByUserId?`, `SubmittedAt?`, `UpdatedAt`, `Version`; `Mark` settable |
| `Grades/GradeStatus.cs` | new enum |
| `Grades/Classification.cs` | unchanged |
| `Results/ResultsPublication.cs` | new entity |
| `Announcements/Announcement.cs`, `Announcements/AnnouncementScope.cs` | new |
| `Audit/AuditEvent.cs`, `Audit/AuditActions.cs` | new; `AuditActions` is a static class of string constants |
| `Settings/AcademicSettings.cs` | new |
| `Users/RushDayRoles.cs` | `Student`, `Lecturer`, `Admin` names and fixed role ids |

Infrastructure-only types: `Identity/ApplicationUser.cs`, `Seeding/DataBackfill.cs`.

## 5. The migration: `20261001120000_PortalAndIdentity`

One migration, generated with `dotnet ef migrations add PortalAndIdentity -p src/RushDay.Infrastructure -s src/RushDay.Api`
after the model changes, then hand-edited so that it is valid on the populated database. Order of operations in `Up`:

1. Create `roles`, `users`, `user_roles`, `user_claims`, `role_claims`, `user_logins`, `user_tokens`, `data_protection_keys`.
2. Create `lecturers`, `module_lecturers`, `academic_settings`, `enrolment_windows`, `results_publications`,
   `announcements`, `audit_events`, `data_backfills`.
3. `students`: add `email`.
4. `modules`: add `department` (default `''`), `description`, `enrolled_count` (default 0), `is_active` (default true),
   `updated_at`; `migrationBuilder.Sql("UPDATE modules SET department = left(code, 2)")`;
   `migrationBuilder.Sql("ALTER TABLE modules ALTER COLUMN department DROP DEFAULT")`; the reconciliation SQL of
   section 6 step 1; add the two CHECK constraints.
5. `enrolments`: add `status` (default `'Active'`, kept), `source` (default `'Seed'`, then drop), `withdrawn_at`,
   `created_by_user_id`, `updated_at`; drop `ix_enrolments_module_id`; create `ix_enrolments_module_id_status`;
   CHECK constraints.
6. `grades`: alter `published_at` nullable; add `status` (default `'Published'`, then drop), `publication_id`,
   `entered_by_user_id`, `submitted_at`, `updated_at` (`defaultValueSql: "now()"`, then drop), `version` (default 1,
   kept); drop `ix_grades_module_id`; create the two new indexes; CHECK constraints.
7. Foreign keys from `users`, `module_lecturers`, `enrolments`, `grades`, `announcements`, `academic_settings`,
   `enrolment_windows`, `results_publications` to their targets.

`Down` reverses every step (drops added columns and tables; restores `published_at NOT NULL` only after
`UPDATE grades SET published_at = now() WHERE published_at IS NULL`). Existing v0 data is untouched by `Down`.

Runtime cost on Neon: every operation is DDL or a set-based UPDATE on 121 module rows; `ADD COLUMN ... DEFAULT`
is a catalogue change in PostgreSQL 11+, so the 80,000-row tables migrate in milliseconds. `MigrateAsync()` runs at
startup exactly as today (`Database:MigrateOnStartup=true` on Render; `--migrate-and-seed` locally).

Model snapshot rule: the snapshot carries no default for `department`, `source`, `grades.status`, `grades.updated_at`
(their defaults exist only during the migration) and carries `HasDefaultValue` for `enrolled_count` (0), `is_active`
(true), `enrolments.status` (`Active`), `version` (1), `pinned` (false), `must_change_password` (false), `is_demo` (false).

## 6. Startup backfills (`Infrastructure/Seeding/StartupBackfills.cs`)

Startup order in `Program.cs` (also under `--migrate-and-seed`): `MigrateAsync()` → `DatabaseSeeder.SeedAsync`
(unchanged behaviour: skips when any student exists; when it does seed, new `Grade` rows get `Status = Published`,
`PublishedAt = options.ResultsDay`, `UpdatedAt = ResultsDay`, and new `Enrolment` rows `Status = Active`,
`Source = Seed`) → `StartupBackfills.RunAsync(db, options, clock, logger)` when `Database:BackfillOnStartup=true`
(default true in Production and under `--migrate-and-seed`; false when the app runs under `dotnet run` in Development
unless set).

Each step is a named, idempotent unit. Steps marked **always** run on every start and are cheap when there is nothing
to do; steps marked **once** are skipped when their `data_backfills` row exists. Every step writes or updates its
`data_backfills` row (`rows_affected`, `completed_at`) inside its own transaction, so a crash between steps resumes at
the failed step on the next start. Each step logs one line: name, rows affected, elapsed.

| # | Name | Mode | What it does |
|---|---|---|---|
| 1 | `reconcile_enrolled_count` | always | `UPDATE modules m SET enrolled_count = c.n FROM (SELECT module_id, count(*) n FROM enrolments WHERE status = 'Active' GROUP BY module_id) c WHERE m.id = c.module_id AND m.enrolled_count <> c.n;` then `UPDATE modules m SET enrolled_count = 0 WHERE enrolled_count <> 0 AND NOT EXISTS (SELECT 1 FROM enrolments e WHERE e.module_id = m.id AND e.status = 'Active');` Logs any module where `enrolled_count > capacity` at Warning (data-quality item on the ops page; the admin decides whether to raise capacity or admin-withdraw). |
| 2 | `roles` | always | `INSERT INTO roles (id, name, normalized_name, concurrency_stamp) VALUES (...) ON CONFLICT (normalized_name) DO NOTHING` for the three fixed roles. |
| 3 | `academic_settings` | once | Insert `id = 1` if absent: `academic_year = '2026/27'`, `institution_name = Branding:InstitutionName` (default `RushDay Demo University`), `institution_short_name = Branding:InstitutionShortName` (default `RushDay`), `time_zone = Branding:TimeZone` (default `Europe/London`). |
| 4 | `enrolment_windows_2026_27` | once | If no window exists for `2026/27`: insert Autumn (`opens 2026-09-14T09:00Z`, `closes 2026-10-02T17:00Z`, `withdrawal 2026-10-30T17:00Z`) and Spring (`opens 2026-09-14T09:00Z`, `closes 2027-01-29T17:00Z`, `withdrawal 2027-02-26T17:00Z`), `created_by_user_id = null`. |
| 5 | `results_publication_autumn_2025_26` | once | If no `results_publications` row for (`2025/26`, 1): insert one with `publish_at = Database:SeedResultsDay` (default `2026-09-28T09:00:00Z`, the seeder's `ResultsDay`), `created_by_user_id = null`, `note = 'Seeded autumn results'`, counts computed; then `UPDATE grades SET publication_id = @id WHERE publication_id IS NULL AND status = 'Published'`. |
| 6 | `lecturers_and_assignments` | once | If `lecturers` is empty: create 40 lecturers deterministically (`Random(4242)`, names from `DatabaseSeeder`'s first/last name lists, titles `Dr` except every fourth `Prof`), `L00001`–`L00010` CS, `L00011`–`L00020` MA, `L00021`–`L00030` PH, `L00031`–`L00040` EE; `L00001` is fixed as `Dr Aisha Khan`. Assignments: for each module ordered by code, within its department index `i`, leader = `dept[i % 10]`; every level-3 module also gets `Teacher = dept[(i + 5) % 10]`; **CS3099's leader is forced to `L00001`** (its round-robin leader becomes the teacher instead) so the demo lecturer owns the interesting module. |
| 7 | `admin_account` | always | If no user holds role `Admin`: create `admin` (`display_name = 'Demo Administrator'` when demo, else `'Administrator'`). Password: `Bootstrap:AdminPassword` if set (then `must_change_password = true`); else the demo password when `Demo:Enabled`; else **skip** and log a Warning `No administrator account exists; set Bootstrap__AdminPassword and restart.` |
| 8 | `demo_accounts` | always, only when `Demo:Enabled` | Compute one hash per role with `new PasswordHasher<ApplicationUser>().HashPassword(new ApplicationUser(), password)`. Then set-based SQL (below) inserts a `Student` user for every student without one and a `Lecturer` user for every lecturer without one, all `is_demo = true`, `must_change_password = false`, `lockout_enabled = true`. Idempotent by the `NOT EXISTS` predicate, so a half-finished run resumes. Runs in well under 5 s on 0.1 CPU because nothing loops in C#. |
| 9 | `demo_announcements` | once, only when `Demo:Enabled` | Two university announcements by `admin` (`Autumn 2025/26 results publish on 28 September at 10:00`, pinned; `Spring 2026/27 enrolment is open until 29 January`) and one on CS3099 by `L00001` (`Welcome to Advanced Machine Learning`). |

Step 8 SQL (parameters bound with Npgsql, never interpolated):

```sql
INSERT INTO users (id, user_name, normalized_user_name, email, normalized_email, email_confirmed,
                   password_hash, security_stamp, concurrency_stamp, phone_number, phone_number_confirmed,
                   two_factor_enabled, lockout_end, lockout_enabled, access_failed_count,
                   display_name, student_id, lecturer_id, must_change_password, is_demo, created_at, disabled_at, last_login_at)
SELECT gen_random_uuid(), s.student_number, upper(s.student_number), NULL, NULL, false,
       @studentHash, upper(replace(gen_random_uuid()::text, '-', '')), gen_random_uuid()::text, NULL, false,
       false, NULL, true, 0,
       s.full_name, s.id, NULL, false, true, now(), NULL, NULL
FROM students s
WHERE NOT EXISTS (SELECT 1 FROM users u WHERE u.student_id = s.id);

INSERT INTO user_roles (user_id, role_id)
SELECT u.id, @studentRoleId FROM users u
WHERE u.student_id IS NOT NULL AND NOT EXISTS (SELECT 1 FROM user_roles ur WHERE ur.user_id = u.id);
```

The lecturer statement is identical with `lecturers l`, `l.staff_number`, `l.full_name`, `lecturer_id = l.id`,
`@lecturerHash`, `@lecturerRoleId`. Sharing one salt across demo accounts is acceptable for public demo credentials and
is confined to rows with `is_demo = true`; a real deployment never runs step 8.

## 7. Demo accounts

Shown on the login page from `GET /api/public/status` (`demo.accounts`) only when `Demo:Enabled = true`
(true on the Render demo through `Demo__Enabled`, false by default).

| Role | Username | Password | Hint shown on the login page |
|---|---|---|---|
| Student | `S000001` (any of `S000001`–`S020000` works) | `Student-Demo-2026!` | 60 autumn credits, results publish 28 Sep 2026 at 10:00 (Europe/London), can enrol on CS3099 |
| Lecturer | `L00001` | `Lecturer-Demo-2026!` | Leads CS3099 (30 places) and other CS modules; enters and submits marks |
| Administrator | `admin` | `Admin-Demo-2026!` | Windows, results publication, accounts, audit, ops |

Constants live in `Infrastructure/Seeding/DemoAccounts.cs`. Usernames are case-insensitive (`s000001` works).

## 8. Password policy (Identity options + `RushDayPasswordValidator`)

- `RequiredLength = 12`; maximum 128 characters enforced by request validation; `RequireDigit`, `RequireUppercase`,
  `RequireLowercase`, `RequireNonAlphanumeric` all false; `RequiredUniqueChars = 4`.
- `RushDayPasswordValidator` (an `IPasswordValidator<ApplicationUser>`): rejects when the password contains the
  username (case-insensitive), contains `rushday` (case-insensitive), or is in `BlockedPasswords.Set`, a static
  `HashSet<string>` of about 60 common long passwords (`password1234`, `123456789012`, `qwertyuiop123`, `iloveyou1234`,
  `administrator`, `letmein12345`, ...). Error codes map to 400 `weak-password` with `errors.newPassword[]`.
- Hasher: Identity default (PBKDF2-HMAC-SHA512, 100,000 iterations, V3 format). The backfill uses the same hasher so
  `PasswordSignInAsync` verifies.
- Lockout: `AllowedForNewUsers = true`, `MaxFailedAccessAttempts = 5`, `DefaultLockoutTimeSpan = 15 minutes`.

## 9. Local verification without Docker

Prerequisites: PostgreSQL 18 on `localhost:5432` with the seeded `rushday` database (`scripts/seed.ps1`).

1. Clone the seeded database for a rehearsal (PowerShell, `psql` on PATH):
   `psql -U postgres -h localhost -c "DROP DATABASE IF EXISTS rushday_migtest WITH (FORCE)" -c "CREATE DATABASE rushday_migtest TEMPLATE rushday OWNER rushday"`.
2. Run the startup path against it twice:
   `$env:ConnectionStrings__RushDay="Host=localhost;Database=rushday_migtest;Username=rushday;Password=rushday"; $env:Demo__Enabled="true"; dotnet run --project src/RushDay.Api -- --migrate-and-seed` (twice).
3. Assert in `psql -d rushday_migtest`:
   - `SELECT count(*) FROM users;` = 20,041 (20,000 students + 40 lecturers + admin) after both runs.
   - `SELECT count(*) FROM data_backfills;` = 9 with demo on, 7 with demo off (steps 8 and 9 do not run; step 7 always writes its row, with `rows_affected = 0` and `notes = 'skipped: no password'` when it created nothing).
   - `SELECT code, capacity, enrolled_count, (SELECT count(*) FROM enrolments e WHERE e.module_id = m.id AND e.status = 'Active') FROM modules m WHERE code = 'CS3099';` shows `enrolled_count` equal to the count (154 locally, a Warning logged because it exceeds 30).
   - `SELECT status, count(*) FROM grades GROUP BY status;` = `Published 80000`; `SELECT count(*) FROM grades WHERE publication_id IS NULL;` = 0.
   - `SELECT count(*) FROM module_lecturers WHERE role = 'Leader';` = 121; `SELECT l.staff_number FROM module_lecturers ml JOIN lecturers l ON l.id = ml.lecturer_id JOIN modules m ON m.id = ml.module_id WHERE m.code = 'CS3099' AND ml.role = 'Leader';` = `L00001`.
4. `dotnet ef migrations script --idempotent -p src/RushDay.Infrastructure -s src/RushDay.Api -o $env:TEMP/rushday.sql`
   produces a script with no errors; review it for the DROP DEFAULT statements.
5. `scripts/reset-db.ps1` (drop, recreate, `--migrate-and-seed`) still converges to identical data from empty.
6. Integration test `Persistence/MigrationOnSeededDatabaseTests` (stage S11) automates 1–3: applies `InitialCreate`,
   seeds 300 students with `DatabaseSeeder`, applies `PortalAndIdentity`, runs `StartupBackfills` twice, asserts equal
   counts and that the second run affected zero rows in steps 2, 6, 8 and 9.
