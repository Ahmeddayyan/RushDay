# RushDay v1 specification: 01. Domain and data

Scope: entities, tables, columns, constraints, indexes, the EF migrations, the idempotent startup backfills
that run against the already-populated Neon database, and the demo accounts. Decisions D1–D3, D7–D11, D17,
D22, D24 and D27–D32 of `00-overview.md` apply.

What exists today (migration `20260927150750_InitialCreate`): `students`, `modules`, `timetable_slots`, `enrolments`,
`grades`, all `uuid` keyed, snake_case, seeded deterministically (`DatabaseSeeder`, `Random(42)`): 20,000 students,
121 modules (60 autumn with capacity 1,500, 60 spring with capacity 300, plus `CS3099` "Advanced Machine Learning",
spring, capacity 30), 80,000 autumn enrolments (4 per student, 15 credits each, `enrolled_at` in the week of
2025-09-15) and 80,000 grades all `published_at = 2026-09-28T09:00:00Z`. The local database additionally holds 154
enrolments on CS3099 from the v0 load run (oversold by 124, `enrolled_at` 2026-09-27); the Neon database may hold a
similar number from demo visitors. Nothing is re-seeded.

## 1. Conventions

- Identifiers snake_case via `UseSnakeCaseNamingConvention()`; new Identity tables renamed with `ToTable`.
- Primary keys `uuid` from `Guid.CreateVersion7()` (time-ordered) unless stated; rows created by set-based SQL in the
  backfills use `gen_random_uuid()`; instants `timestamp with time zone` (`DateTimeOffset` in C#); enum-like columns
  `varchar` holding the PascalCase C# enum name (EF `HasConversion<string>()`) with a CHECK constraint;
  `modules.semester` stays `integer` (1 Autumn, 2 Spring).
- Every new NOT NULL column on an existing table is added with a migration-time default so the populated database
  migrates in one statement; the default is then dropped unless the table below says "default kept".
- Foreign keys to `users` are `ON DELETE RESTRICT` (users are never deleted; they are disabled). Foreign keys from
  child rows to `modules`/`students` keep the existing `CASCADE`.
- Domain entities live in `src/RushDay.Domain`; `ApplicationUser` lives in `src/RushDay.Infrastructure/Identity`
  because the login store is a persistence concern.
- Academic years are the 7-character label `yyyy/yy` (`2025/26`); the current one lives in `academic_settings`.

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
| email_confirmed, phone_number, phone_number_confirmed | Identity defaults | unused in v1, kept |
| two_factor_enabled | boolean | true once the user verified a TOTP code (`02-api.md` section 2.4); the authenticator key lives in `user_tokens` |
| password_hash, security_stamp, concurrency_stamp | text null | Identity-managed |
| lockout_end | timestamptz null | Identity lockout; admin "lock" sets it to `new DateTimeOffset(9999, 12, 31, 0, 0, 0, TimeSpan.Zero)` (serialised `9999-12-31T00:00:00Z`) |
| lockout_enabled | boolean | true for every user; demo accounts are never counted toward lockout (`02-api.md` section 2.3) |
| access_failed_count | integer | Identity |
| **display_name** | varchar(200) not null | shown in the top bar and audit log; kept in sync with `students.full_name` / `lecturers.full_name` by the admin edit routes |
| **student_id** | uuid null | FK `students(id)` RESTRICT; unique index `ix_users_student_id` |
| **lecturer_id** | uuid null | FK `lecturers(id)` RESTRICT; unique index `ix_users_lecturer_id` |
| **must_change_password** | boolean not null default false | provisioned and reset accounts start true; demo accounts false |
| **is_demo** | boolean not null default false | created by the demo backfill (or the bootstrap step with the demo password); healed on every demo start, disabled on the first start with demo off, never lockable or mutable through the API (409 `demo-account`) |
| **created_at** | timestamptz not null | |
| **disabled_at** | timestamptz null | disabled users cannot sign in; existing sessions die at the next security-stamp check |
| **last_login_at** | timestamptz null | updated on successful login (after the second factor when one applies) |
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
| `user_tokens` | `user_id`, `login_provider`, `name`, `value`; PK (user_id, login_provider, name). Holds the TOTP secret (`[AspNetUserStore]` / `AuthenticatorKey`, written by `UserManager.ResetAuthenticatorKeyAsync`), the last accepted TOTP time step (`[RushDay]` / `LastTotpStep`, so a code is accepted once, `02-api.md` section 2.4) and, Should, `RecoveryCodes`. Rows for `is_demo` users are deleted by the demo heal step. |
| `data_protection_keys` | `id integer identity PK`, `friendly_name text`, `xml text` (from `Microsoft.AspNetCore.DataProtection.EntityFrameworkCore`); the `xml` is AES-256-GCM encrypted under `DataProtection:KeyEncryptionKey` outside Development (`02-api.md` section 2.1), so cookies survive redeploys and a database dump cannot mint them |

## 3. Domain tables

### `students` (existing; changed)

Add `email varchar(256) null` and `left_at timestamptz null` (set by `POST /api/admin/students/{n}/leave`; a student
with `left_at` cannot be enrolled by anyone, 409 `student-left`, and gets no login: provisioning or re-enabling an
account for them is 409 `principal-left`). Everything else unchanged (`student_number varchar(16)` unique, `full_name`,
`programme`, `year_of_study`).

### `lecturers` (new)

| Column | Type | Constraints |
|---|---|---|
| id | uuid | PK |
| staff_number | varchar(16) not null | unique `ix_lecturers_staff_number`; format `L#####` |
| full_name | varchar(200) not null | |
| title | varchar(16) not null | `Dr`, `Prof`, `Mr`, `Ms`, `Mx` |
| department | varchar(8) not null | `CS`, `MA`, `PH`, `EE` |
| email | varchar(256) null | |
| left_at | timestamptz null | set by `POST /api/admin/lecturers/{staffNumber}/leave`; a lecturer with `left_at` cannot be assigned to a module (422 `invalid-lecturer-assignment`) and gets no account (409 `principal-left`); existing assignments stay and are shown with a "left" badge but carry no authority: every lecturer lookup (`TeachesModule`, `StaffModules.TaughtAsync`, the lecturer module list) requires `left_at IS NULL` (review S6 E5) |

### `module_lecturers` (new)

| Column | Type | Constraints |
|---|---|---|
| module_id | uuid | FK `modules(id)` CASCADE |
| lecturer_id | uuid | FK `lecturers(id)` CASCADE |
| role | varchar(16) not null | CHECK `ck_module_lecturers_role` in (`Leader`, `Teacher`) |
| assigned_at | timestamptz not null | |
| assigned_by_user_id | uuid null | FK `users(id)` RESTRICT; null = seed |
| PK (module_id, lecturer_id); index `ix_module_lecturers_lecturer_id` | | exactly one `Leader` per module is a service rule; only the Leader may submit the module's marks (`02-api.md` section 8.4) |
| Unique index `ix_module_lecturers_module_id_leader` on (module_id) WHERE `role = 'Leader'` | | migration `ResultsGovernance` (section 5a, review S6 E10): at most one leader per module at the database, whatever two concurrent assignments do; the assignment route also locks the module row first and saves demotions before the new leader |

### `modules` (existing; changed)

| Added column | Type | Migration handling |
|---|---|---|
| department | varchar(8) not null | add with default `''`, `UPDATE modules SET department = left(code, 2)`, drop default |
| description | varchar(2000) null | |
| enrolled_count | integer not null | default `0` **kept** (model `HasDefaultValue(0)`). Means: the number of enrolments with `status = 'Active'` **and `academic_year` = `academic_settings.academic_year`** (D28). Computed by the last backfill step (`reconcile_enrolled_count`, section 6), which needs the settings row, so the migration itself does not reconcile; maintained by the enrolment and withdrawal transactions; recomputed in the same transaction when `PUT /api/admin/settings` changes the academic year |
| is_active | boolean not null | default `true` kept |
| updated_at | timestamptz null | |
| CHECK `ck_modules_enrolled_count_non_negative` | | `enrolled_count >= 0`. There is deliberately no `<= capacity` CHECK (D11). |
| CHECK `ck_modules_capacity_positive` | | `capacity >= 0` |

Existing: `code varchar(16)` unique, `title varchar(200)`, `credits`, `capacity`, `semester integer`. Editing a module
may leave `capacity` unchanged even while `enrolled_count > capacity` (drift inherited from v0); only a request that
lowers `capacity` below `enrolled_count` is rejected (422 `capacity-below-enrolled`).

### `enrolments` (existing; changed)

| Added column | Type | Migration handling |
|---|---|---|
| status | varchar(16) not null | CHECK `ck_enrolments_status` in (`Active`, `Withdrawn`); default `'Active'` kept (model `HasDefaultValue`) |
| source | varchar(16) not null | CHECK `ck_enrolments_source` in (`Seed`, `Self`, `Admin`); default `'Seed'` **kept** (model `HasDefaultValue(EnrolmentSource.Seed)`) so an insert by the v0 container during Render's deploy overlap still succeeds; the next migration may drop it |
| academic_year | varchar(9) not null | add with default `'2025/26'` (every seeded row is a 2025/26 autumn enrolment), then drop the default; the v0 load-run and demo-visitor rows on CS3099 are relabelled `2026/27` by backfill step 4; set from `academic_settings.academic_year` on every insert and reactivation (`04-performance-and-ops.md` section 2.1) |
| withdrawn_at | timestamptz null | |
| created_by_user_id | uuid null | FK `users(id)` RESTRICT; null for seed and for self-enrolments (the student is the actor; the audit row names them) |
| updated_at | timestamptz null | |
| Index `ix_enrolments_module_id_status` (module_id, status) | | replaces `ix_enrolments_module_id` (dropped) |
| Index `ix_enrolments_student_id_academic_year_status` (student_id, academic_year, status) | | the dashboard and credit-budget path |

Kept: unique `ix_enrolments_student_id_module_id` (the row toggles status instead of being deleted, D10), `enrolled_at`.
A reactivation stamps the current academic year on the row; a module the student already holds a Submitted or
Published grade for cannot be reactivated (409 `results-exist`, D28), no enrolment or reactivation is made on a module
whose marks for the current year have left draft (409 `module-locked`, `02-api.md` section 8.3, review S6 E1), and
reactivating a row from an earlier year deletes
that module's Draft grade for the student in the same transaction (`04-performance-and-ops.md` section 2.1 step 6), so a
grade's year is always its enrolment's year.

### `grades` (existing; changed)

| Column | Type | Migration handling |
|---|---|---|
| mark | integer **null** | `AlterColumn` to nullable; null iff `outcome <> 'Mark'` |
| outcome | varchar(16) not null | CHECK `ck_grades_outcome` in (`Mark`, `Absent`, `Deferred`); default `'Mark'` **kept** (model `HasDefaultValue(GradeOutcome.Mark)`) |
| status | varchar(16) not null | CHECK `ck_grades_status` in (`Draft`, `Submitted`, `Published`); add with default `'Published'` (every existing row is a published mark), drop default |
| published_at | timestamptz **null** | `AlterColumn` to nullable; existing values kept |
| publication_id | uuid null | FK `results_publications(id)` RESTRICT; set by backfill step 3 for existing rows; NULL again after cancel or unpublish |
| entered_by_user_id | uuid null | FK `users(id)` RESTRICT |
| submitted_at | timestamptz null | |
| updated_at | timestamptz not null | add with `defaultValueSql: "now()"`, drop default |
| version | integer not null | default `1` kept; incremented on every mark, outcome or status change (optimistic concurrency for the marks grid) |
| corrected_at | timestamptz null | set by the administrator's correction route (`02-api.md` section 8.5); shown to the student as "Amended {date}" |
| CHECK `ck_grades_mark_range` | | `mark IS NULL OR (mark >= 0 AND mark <= 100)` |
| CHECK `ck_grades_mark_outcome` | | `(outcome = 'Mark') = (mark IS NOT NULL)` |
| CHECK `ck_grades_published_has_instant` | | `status <> 'Published' OR published_at IS NOT NULL` |
| Index `ix_grades_student_id_status_published_at` (student_id, status, published_at) | | the results-day read path |
| Index `ix_grades_module_id_status` (module_id, status) | | replaces `ix_grades_module_id` (dropped) |

Kept: unique `ix_grades_student_id_module_id`. A grade has no academic year of its own: reads join
`enrolments` on (`student_id`, `module_id`) to get it.

### `academic_settings` (new, singleton)

| Column | Type | Constraints |
|---|---|---|
| id | integer | PK, CHECK `ck_academic_settings_singleton` (`id = 1`) |
| academic_year | varchar(9) not null | `2026/27` |
| institution_name | varchar(200) not null | `RushDay Demo University` |
| institution_short_name | varchar(32) not null | `RushDay` |
| time_zone | varchar(64) not null | IANA id as text, `Europe/London`; validated by shape (`02-api.md` section 8.5), formatted only by the browser |
| current_semester | integer not null | CHECK `ck_academic_settings_current_semester` in (1, 2); `1` (Autumn) on insert; decides which modules' slots the student timetable shows |
| support_email | varchar(256) null | the academic office's address, rendered wherever the UI says "contact the academic office" |
| support_url | varchar(400) null | the academic office's help page (same use; either or both may be set) |
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
| academic_year | varchar(9) not null | the year of the enrolments whose grades it published |
| semester | integer not null | CHECK in (1, 2) |
| publish_at | timestamptz not null | the instant grades become visible; may be rescheduled while `publish_at > now` |
| created_at | timestamptz not null | |
| created_by_user_id | uuid null | FK `users(id)` RESTRICT; null = seed |
| grade_count, module_count | integer not null | counts at publication time, decremented by return-to-draft while scheduled |
| note | varchar(400) null | |
| announcement_id | uuid null | FK `announcements(id)` ON DELETE SET NULL, index `ix_results_publications_announcement_id`; migration `ResultsGovernance` (section 5a, review S6 E2): the pinned "results are available" announcement a publish with `announce` posted, which a reschedule moves and a cancel, an unpublish or an emptying return to draft soft-deletes; null when the publish did not announce and for the seed publication |
| Index `ix_results_publications_semester_created_at` (academic_year, semester, created_at DESC) | | |

`state` is derived by reads: `scheduled` when `publish_at > now`, else `live`. Cancelling a scheduled publication or
unpublishing a live one deletes the row after reverting its grades (`02-api.md` section 8.5), and so does a return to
draft that takes the last grade out of a scheduled one (review S6 E3); the audit row keeps the history.

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

### `audit_events` (new, append-only at the database)

| Column | Type | Notes |
|---|---|---|
| id | uuid | PK, v7 (time-ordered) |
| occurred_at | timestamptz not null | |
| actor_user_id | uuid null | null = system/startup |
| actor_username | varchar(64) null | denormalised for display |
| actor_role | varchar(16) null | |
| action | varchar(64) not null | one of `AuditActions` (`03-security.md` section 7) |
| subject_type | varchar(32) not null | one of `AuditSubjects`: `Enrolment`, `Grade`, `Module`, `Publication`, `Announcement`, `Account`, `Settings`, `Window`, `Student`, `Lecturer`, `System` |
| subject_id | varchar(64) null | uuid, module code, student number or staff number as text |
| student_id, module_id | uuid null | denormalised for filtering |
| details | jsonb null | before/after values, reason, decision; serialised by `AuditWriter` |
| request_id | varchar(64) null | `Activity.Current?.Id ?? HttpContext.TraceIdentifier` |
| ip_hash | varchar(64) null | `IpHasher.Hash(clientIp)` = `hex(HMAC-SHA256(dailyKey, clientIp))[..32]`, D32; null for startup steps |
| chain_hash | varchar(64) null | **Should**: `sha256(previous chain_hash \|\| id \|\| occurred_at \|\| action \|\| details)` computed by `AuditWriter` under the table's advisory lock, for tamper evidence; null until built |
| Indexes | | `(occurred_at DESC)`, `(student_id, occurred_at DESC)`, `(module_id, occurred_at DESC)`, `(actor_user_id, occurred_at DESC)`, `(action, occurred_at DESC)` |

Immutability is enforced by PostgreSQL, not by convention: the migration creates

```sql
CREATE FUNCTION audit_events_immutable() RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN RAISE EXCEPTION 'audit_events is append-only'; END $$;
CREATE TRIGGER trg_audit_events_immutable BEFORE UPDATE OR DELETE ON audit_events
  FOR EACH ROW EXECUTE FUNCTION audit_events_immutable();
```

so the application role, a raw-SQL mistake or an injected statement cannot rewrite or delete the trail
(`AuditTests.Update_and_delete_are_rejected_by_the_database`). Row triggers do not fire on `TRUNCATE`, which is why the
application role `rushday_app` is granted DML without `TRUNCATE` (`03-security.md` section 8); the owner role, used
only by migrations, remains able to drop the trigger (the residual risk of a stolen owner password, recorded in
`docs/deployment.md`). The application also has no EF entity method that would
update or delete an audit row. A future retention purge (Could) must drop and recreate the trigger inside its own
audited transaction.

### `data_backfills` (new)

| Column | Type |
|---|---|
| name | varchar(64) PK |
| completed_at | timestamptz not null |
| rows_affected | integer not null |
| notes | varchar(400) null |

Shown on the admin ops page with a human label per step (`05-frontend.md` section 10).

## 4. Domain model changes (`src/RushDay.Domain`)

Lifecycle properties become settable (`{ get; set; }`); identity and natural-key properties stay `init`.

| File | Change |
|---|---|
| `Students/Student.cs` | add `string? Email`, `DateTimeOffset? LeftAt`; `FullName`, `Programme`, `YearOfStudy` become settable (admin edit); `StudentNumber` stays `init` |
| `Lecturers/Lecturer.cs` | new: `Id`, `StaffNumber`, `FullName`, `Title`, `Department`, `Email?`, `LeftAt?` |
| `Modules/Module.cs` | add `Department`, `Description?`, `EnrolledCount` (set), `IsActive` (set), `UpdatedAt?` (set), computed `Level => Code[2] - '0'`; `Title`, `Credits`, `Capacity`, `Semester` become settable |
| `Modules/ModuleLecturer.cs`, `Modules/ModuleLecturerRole.cs` | new: `ModuleId`, `LecturerId`, `Role` (`Leader`, `Teacher`), `AssignedAt`, `AssignedByUserId?` |
| `Enrolments/Enrolment.cs` | add `Status` (`EnrolmentStatus.Active/Withdrawn`), `Source` (`EnrolmentSource.Seed/Self/Admin`), `AcademicYear` (string), `WithdrawnAt?`, `CreatedByUserId?`, `UpdatedAt?`; `EnrolledAt` settable (reactivation) |
| `Enrolments/EnrolmentRules.cs` | `Evaluate(Module module, int currentEnrolledCount, int studentCreditsInSemester, bool alreadyEnrolled, bool windowOpen, bool ignoreCreditLimit = false)`; order: `AlreadyEnrolled`, `WindowClosed` (only when `!windowOpen`), `CreditLimitExceeded` (only when `!ignoreCreditLimit`), `ModuleFull`, `Accepted`. The admin override passes `windowOpen: true, ignoreCreditLimit: true`. `MaxCreditsPerSemester = 60` unchanged. The "already completed" rule (`results-exist`) is a data check in `EnrolmentService`, not a rule here. |
| `Enrolments/EnrolmentDecision.cs` | add `WindowClosed` |
| `Enrolments/EnrolmentWindow.cs` | new entity with pure `bool IsOpenAt(DateTimeOffset now)` and `bool AllowsWithdrawalAt(DateTimeOffset now)` |
| `Grades/Grade.cs` | `Mark` becomes `int?` and settable; `PublishedAt` becomes `DateTimeOffset?`; add `Outcome` (`GradeOutcome.Mark/Absent/Deferred`), `Status` (`GradeStatus.Draft/Submitted/Published`), `PublicationId?`, `EnteredByUserId?`, `SubmittedAt?`, `UpdatedAt`, `Version`, `CorrectedAt?` |
| `Grades/GradeStatus.cs`, `Grades/GradeOutcome.cs` | new enums |
| `Grades/Classification.cs` | `WeightedAverage` is unchanged and takes only graded results; add `Graded(IEnumerable<(GradeOutcome Outcome, int? Mark, int Credits)>)` returning the `(Mark, Credits)` pairs whose `Outcome == Mark` (absences and deferrals never count), and `Band(int mark)` = `FromAverage(mark)` (the band label of one mark) |
| `Results/ResultsPublication.cs` | new entity; `AnnouncementId?` (settable) since migration `ResultsGovernance` (section 5a) |
| `Announcements/Announcement.cs`, `Announcements/AnnouncementScope.cs` | new |
| `Audit/AuditEvent.cs`, `Audit/AuditActions.cs`, `Audit/AuditSubjects.cs` | new; `AuditEvent` carries `ChainHash?` (Should, unused until built); `AuditActions` and `AuditSubjects` are static classes of string constants covering exactly the catalogue of `03-security.md` section 7; `AuditActions.SubjectOf(action)` maps every action to its subject (unit-tested: every constant maps to a listed subject) |
| `Settings/AcademicSettings.cs` | new: `Id`, `AcademicYear`, `InstitutionName`, `InstitutionShortName`, `TimeZone`, `CurrentSemester` (`Semester`, stored as integer), `SupportEmail?`, `SupportUrl?`, `UpdatedAt`, `UpdatedByUserId?` |
| `Users/RushDayRoles.cs` | `Student`, `Lecturer`, `Admin` names and fixed role ids |

Infrastructure-only types: `Identity/ApplicationUser.cs`, `Identity/PasswordHashing.cs` (the one PBKDF2 iteration
count, section 8), `Seeding/DataBackfill.cs`, `Seeding/StartupBackfillOptions.cs`.

## 5. The migration: `20261001120000_PortalAndIdentity`

One migration, generated with `dotnet ef migrations add PortalAndIdentity -p src/RushDay.Infrastructure -s src/RushDay.Api`
after the model changes. `dotnet ef` stamps the current time, so rename the generated files to
`20261001120000_PortalAndIdentity.cs` and `20261001120000_PortalAndIdentity.Designer.cs` and set
`[Migration("20261001120000_PortalAndIdentity")]` in the Designer file; `dotnet ef migrations list` must show it after
`InitialCreate`. Then hand-edit `Up` so that it is valid on the populated database, in exactly this order:

1. Create `roles`, `users`, `user_roles`, `user_claims`, `role_claims`, `user_logins`, `user_tokens`, `data_protection_keys`
   (without the FKs from `users` to `students`/`lecturers`; those come in step 7).
2. Create `lecturers` (with `left_at`), `module_lecturers`, `academic_settings` (with `current_semester`,
   `support_email`, `support_url` and `ck_academic_settings_current_semester`), `enrolment_windows`,
   `results_publications`, `announcements`, `audit_events` (with `chain_hash`), `data_backfills`; then
   `migrationBuilder.Sql(...)` for the `audit_events_immutable` function and the `trg_audit_events_immutable` trigger
   (section 3), immediately after `audit_events` exists.
3. `students`: add `email`, `left_at`.
4. `modules`: add `department` (default `''`), `description`, `enrolled_count` (default 0, kept), `is_active` (default
   true, kept), `updated_at`; `migrationBuilder.Sql("UPDATE modules SET department = left(code, 2)")`;
   `migrationBuilder.Sql("ALTER TABLE modules ALTER COLUMN department DROP DEFAULT")`; the two CHECK constraints.
5. `enrolments`: add `status` (default `'Active'`, kept), `source` (default `'Seed'`, kept), `academic_year` (default
   `'2025/26'`, then `ALTER TABLE enrolments ALTER COLUMN academic_year DROP DEFAULT`), `withdrawn_at`,
   `created_by_user_id`, `updated_at`; drop `ix_enrolments_module_id`; create `ix_enrolments_module_id_status` and
   `ix_enrolments_student_id_academic_year_status`; the two CHECK constraints.
6. `grades`: alter `published_at` nullable; alter `mark` nullable; add `outcome` (default `'Mark'`, kept), `status`
   (default `'Published'`, then drop), `publication_id`, `entered_by_user_id`, `submitted_at`, `updated_at`
   (`defaultValueSql: "now()"`, then drop), `version` (default 1, kept), `corrected_at`; drop `ix_grades_module_id`;
   create the two new indexes; the five CHECK constraints (`ck_grades_status`, `ck_grades_outcome`,
   `ck_grades_mark_range`, `ck_grades_mark_outcome`, `ck_grades_published_has_instant`).
7. Foreign keys from `users`, `module_lecturers`, `enrolments`, `grades`, `announcements`, `academic_settings`,
   `enrolment_windows`, `results_publications` to their targets.

The migration contains **no** `enrolled_count` reconciliation. The count is year-scoped (D28) and needs the
`academic_settings` row, which only the backfills create, so the always-run last backfill step computes it on the
same start; until then every module keeps the default 0, which is also the true current-year count of a database
that holds only 2025/26 enrolments. (A reconciliation that filters on `enrolments.status` would also fail with
`column "status" does not exist` if it ran before step 5.)

`Down` reverses the steps in the opposite order (7, 6, 5, 4, 3, 2, 1): it drops the trigger and function
(`DROP TRIGGER IF EXISTS trg_audit_events_immutable ON audit_events; DROP FUNCTION IF EXISTS audit_events_immutable();`)
before the `audit_events` table, restores `published_at NOT NULL` only after `UPDATE grades SET published_at = now()
WHERE published_at IS NULL` and `mark NOT NULL` only after `UPDATE grades SET mark = 0 WHERE mark IS NULL`, and drops
the added columns and tables. Existing v0 data is untouched by `Down`.

Runtime cost on Neon: every operation is DDL or a set-based UPDATE on 121 module rows; `ADD COLUMN ... DEFAULT`
is a catalogue change in PostgreSQL 11+, so the 80,000-row tables migrate in milliseconds. `MigrateAsync()` runs at
startup exactly as today (`Database:MigrateOnStartup=true` on Render; `--migrate-and-seed` locally), on
`ConnectionStrings:Migrations` when it is set and on `ConnectionStrings:RushDay` otherwise (`03-security.md` section 8).

Model snapshot rule: the snapshot carries no default for `department`, `academic_year`, `grades.status`,
`grades.updated_at` (their defaults exist only during the migration) and carries `HasDefaultValue` for
`enrolled_count` (0), `is_active` (true), `enrolments.status` (`Active`), `enrolments.source` (`Seed`), `grades.outcome`
(`Mark`), `version` (1), `pinned` (false), `must_change_password` (false), `is_demo` (false).

## 5a. The second migration: `20261002120000_ResultsGovernance` (review S6 E2, E10)

`PortalAndIdentity` stays exactly as it was rehearsed; the S6 review's two schema changes are a small additive second
migration, generated with `dotnet ef migrations add ResultsGovernance` after the model changes and renamed to the fixed
timestamp like the first (`[Migration("20261002120000_ResultsGovernance")]`; `dotnet ef migrations list` shows it after
`PortalAndIdentity`). `Up`, in order:

1. `results_publications`: add `announcement_id uuid NULL`; create `ix_results_publications_announcement_id`.
2. `UPDATE module_lecturers ml SET role = 'Teacher' WHERE ml.role = 'Leader' AND EXISTS (another Leader row of the
   same module with a smaller (assigned_at, lecturer_id))`: a database that already holds a module with two leaders
   (the race the index closes) keeps the one assigned first; no row is deleted. 0 rows on every rehearsed database.
3. Create the unique index `ix_module_lecturers_module_id_leader` on `module_lecturers (module_id) WHERE role = 'Leader'`.
4. Add `fk_results_publications_announcements_announcement_id` (`announcements(id)`, `ON DELETE SET NULL`:
   announcements are only soft-deleted; the one hard delete, a module's cascade, never reaches a university announcement).

`Down` drops the foreign key, both indexes and the column; it loses only the links (the announcements themselves stay)
and a demoted second leader stays a teacher. Every statement is DDL or an `UPDATE` of a handful of rows; nothing
touches the 80,000-row tables, so it runs in milliseconds on Neon. The model carries both (`ResultsPublication
.AnnouncementId` with `HasOne<Announcement>().OnDelete(SetNull)`; `HasIndex(ModuleId).IsUnique().HasFilter("role =
'Leader'")`), so the snapshot matches.

Rehearsed on 2026-09-29 (section 9, with the Release single-file build): on a `TEMPLATE rushday` clone of the v0
database (`InitialCreate` only, 154 v0 CS3099 rows) the first start applied both migrations and wrote 120,702 backfill
rows, the second 0; every assertion of section 9 held, and the column, the foreign key and both indexes were present.
`dotnet ef database update PortalAndIdentity` removed exactly the column and the two indexes; the `ResultsGovernance`
part of the idempotent script re-applied them and was a no-op the second time; `dotnet ef database update
InitialCreate` then left the v0 data intact (20,000 students, 80,000 grades, 80,254 enrolments). From an empty database
the first start (seed, both migrations, backfills) and the second (0 rows) converged to the section 9 counts with
`2026/27 100`.

## 6. Startup backfills (`Infrastructure/Seeding/StartupBackfills.cs`)

Startup order in `Startup/StartupTasks.RunAsync` (also under `--migrate-and-seed`; until S2 creates `StartupTasks`,
S1's `Program.cs` block does the same): `MigrateAsync()` →
`DatabaseSeeder.SeedAsync` **only when `Demo:Enabled = true` (and `Database:SeedOnStartup`) or under
`--migrate-and-seed`** (unchanged behaviour otherwise: skips when any student exists; when it does seed, new `Grade`
rows get `Status = Published`, `Outcome = Mark`, `PublishedAt = options.ResultsDay`, `UpdatedAt = ResultsDay`, and new
`Enrolment` rows `Status = Active`, `Source = Seed`, `AcademicYear = '2025/26'`) →
`StartupBackfills.RunAsync(RushDayDbContext db, StartupBackfillOptions options, TimeProvider clock, ILogger logger,
CancellationToken ct)` when `Database:BackfillOnStartup=true` (default true outside Development and under
`--migrate-and-seed`; false in Development unless set). A customer database therefore starts with roles, settings,
the bootstrap administrator and nothing synthetic.

`StartupBackfillOptions` (`Infrastructure/Seeding/StartupBackfillOptions.cs`, owned by S1) is a record bound in
`Program.cs` from `Demo:Enabled`, `Bootstrap:AdminUsername`, `Bootstrap:AdminPassword`, `Branding:*` and
`Database:SeedResultsDay`:

```csharp
public sealed record StartupBackfillOptions
{
    public bool DemoEnabled { get; init; }
    public string BootstrapAdminUsername { get; init; } = "admin";
    public string? BootstrapAdminPassword { get; init; }
    public string InstitutionName { get; init; } = "RushDay Demo University";
    public string InstitutionShortName { get; init; } = "RushDay";
    public string TimeZone { get; init; } = "Europe/London";
    public DateTimeOffset SeedResultsDay { get; init; } = new(2026, 9, 28, 9, 0, 0, TimeSpan.Zero);
}
```

S2's `Options/*` classes keep those section names and `StartupTasks` builds the same record.

Each step is a named, idempotent unit. Steps marked **always** run on every start and are cheap when there is nothing
to do; steps marked **once** are skipped when their `data_backfills` row exists. Steps marked **demo** run only when
`Demo:Enabled`; **demo off** only when it is not. Every step writes or updates its `data_backfills` row
(`rows_affected`, `completed_at`) inside its own transaction, so a crash between steps resumes at the failed step on
the next start. Each step logs one line: name, rows affected, elapsed. The reconciliation step runs **last** so it sees
every demo step's changes.

| # | Name | Mode | What it does |
|---|---|---|---|
| 1 | `roles` | always | `INSERT INTO roles (id, name, normalized_name, concurrency_stamp) VALUES (...) ON CONFLICT (normalized_name) DO NOTHING` for the three fixed roles. |
| 2 | `academic_settings` | once | Insert `id = 1` if absent: `academic_year = '2026/27'`, `institution_name = options.InstitutionName`, `institution_short_name = options.InstitutionShortName`, `time_zone = options.TimeZone`, `current_semester = 1`, `support_email = NULL`, `support_url = NULL`. |
| 3 | `results_publication_autumn_2025_26` | once | If any grade has `status = 'Published' AND publication_id IS NULL` and no `results_publications` row exists for (`2025/26`, 1): insert one with `publish_at = options.SeedResultsDay`, `created_by_user_id = null`, `note = 'Seeded autumn results'`, counts computed; then `UPDATE grades SET publication_id = @id WHERE publication_id IS NULL AND status = 'Published'`. Affects 0 rows on a customer database. |
| 4 | `relabel_v0_self_enrolments` | once | `UPDATE enrolments SET source = 'Self', academic_year = '2026/27' WHERE source = 'Seed' AND enrolled_at >= '2026-09-01' AND module_id = (SELECT id FROM modules WHERE code = 'CS3099')` (the v0 load-run and demo-visitor rows, made during the 2026/27 enrolment window; seed rows are dated September 2025). 0 rows on a customer database. |
| 5 | `admin_account` | always | If no **usable** administrator exists (`NOT EXISTS` a user with role `Admin`, `disabled_at IS NULL`, and `NOT is_demo OR options.DemoEnabled`): create `options.BootstrapAdminUsername` (`display_name = 'Demo Administrator'` when the demo password is used, else `'Administrator'`). If that username already exists, log a Warning `Bootstrap username '{name}' is taken; set Bootstrap__AdminUsername to another name` and skip. Password: `options.BootstrapAdminPassword` if set, first checked with `RushDayPasswordValidator.Check(username, password)` (section 8; on failure skip creation and log `Bootstrap password rejected by policy: {codes}`), then `must_change_password = true`, `is_demo = false`; else the demo password when `options.DemoEnabled` (`is_demo = true`, `must_change_password = false`); else **skip** and log a Warning `No administrator account exists; set Bootstrap__AdminPassword and restart.` Hashes with `PasswordHashing.Create()` (210,000 iterations). Audits `account.provisioned { username, role: 'Admin' }` with a null actor when it creates. Writes its row with `rows_affected = 0` and `notes = 'skipped: ...'` when it created nothing. |
| 6 | `enrolment_windows_2026_27` | always, demo | Keeps the demo's two windows in place: `INSERT ... ON CONFLICT (academic_year, semester) DO UPDATE SET opens_at = EXCLUDED.opens_at, closes_at = EXCLUDED.closes_at, withdrawal_deadline_at = EXCLUDED.withdrawal_deadline_at, updated_at = now() WHERE (enrolment_windows.opens_at, enrolment_windows.closes_at, enrolment_windows.withdrawal_deadline_at) IS DISTINCT FROM (EXCLUDED.opens_at, EXCLUDED.closes_at, EXCLUDED.withdrawal_deadline_at)` for Autumn 2026/27 (`opens 2026-09-14T09:00Z`, `closes 2026-10-02T17:00Z`, `withdrawal 2026-10-30T17:00Z`) and Spring 2026/27 (`opens 2026-09-14T09:00Z`, `closes 2027-01-29T17:00Z`, `withdrawal 2027-02-26T17:00Z`), `created_by_user_id = null`; then `UPDATE academic_settings SET academic_year = '2026/27', updated_at = now() WHERE id = 1 AND academic_year <> '2026/27'`. A visitor who deletes, closes or moves a window, or changes the year, spoils the demo only until the next start. A customer creates windows from the admin UI. |
| 7 | `lecturers_and_assignments` | once, demo | If `lecturers` is empty: create 40 lecturers deterministically (`Random(4242)`, names from `DatabaseSeeder`'s first/last name lists, titles `Dr` except every fourth `Prof`), `L00001`–`L00010` CS, `L00011`–`L00020` MA, `L00021`–`L00030` PH, `L00031`–`L00040` EE; `L00001` is fixed as `Dr Aisha Khan`. Assignments: for each module ordered by code (ordinal), within its department index `i` (0-based), leader = `dept[i % 10]`; every level-3 module also gets `Teacher = dept[(i + 5) % 10]`. CS3099 is the 31st CS module by code (index 30), so it resolves to leader `L00001` and teacher `L00006` by these rules alone; CS3001 (index 20) resolves to leader `L00001` and teacher `L00006` too. No override is needed. If a future reseed changes the ordering, apply: CS3099 leader = `L00001`; teacher = the round-robin leader unless it is `L00001`, in which case teacher = `dept[(i + 5) % 10]`. A customer creates lecturers from the admin UI. |
| 8 | `demo_accounts` | always, demo | **Hash**: read the stored hash of `S000001`, `L00001` and the demo `admin`; verify each against its role's demo password with `PasswordHashing.Create()`; reuse a stored hash that verifies as `Success`, otherwise (missing, `Failed` or `SuccessRehashNeeded`) compute a new one with `PasswordHashing.Create().HashPassword(new ApplicationUser(), password)` and `UPDATE users SET password_hash = @hash WHERE is_demo AND <role predicate>` (at most six PBKDF2 operations, a few seconds on 0.1 CPU). **Insert**: set-based SQL (below) inserts a `Student` user for every student without one and a `Lecturer` user for every lecturer without one, all `is_demo = true`, `must_change_password = false`, `lockout_enabled = true`. **Heal** every `is_demo` row so a redeploy repairs the public demo: `UPDATE users SET disabled_at = NULL, lockout_end = NULL, access_failed_count = 0, must_change_password = false, two_factor_enabled = false, security_stamp = CASE WHEN disabled_at IS NOT NULL THEN upper(replace(gen_random_uuid()::text, '-', '')) ELSE security_stamp END WHERE is_demo AND (disabled_at IS NOT NULL OR lockout_end IS NOT NULL OR access_failed_count <> 0 OR must_change_password OR two_factor_enabled)` and `DELETE FROM user_tokens WHERE user_id IN (SELECT id FROM users WHERE is_demo)`. Idempotent by the `NOT EXISTS` predicates and `WHERE` filters, so a half-finished run resumes and a clean start touches 0 rows. Nothing loops in C#. |
| 9 | `demo_accounts_disable` | always, demo off | `UPDATE users SET disabled_at = now(), lockout_end = NULL, security_stamp = upper(replace(gen_random_uuid()::text, '-', '')) WHERE is_demo AND disabled_at IS NULL`; logs the count at Warning (`Disabled {n} demo accounts because Demo:Enabled is false`) and audits `system.demo_accounts_disabled { count }` when `n > 0`. Step 5 has already run on this start and, because a demo administrator is not usable with demo off, has created the bootstrap administrator (or logged why it could not). |
| 10 | `demo_announcements` | once, demo | Two university announcements by the demo `admin` and one on CS3099 by `L00001`. The text never contains a formatted date (D26); it is chosen from the data at insert time: the pinned results notice reads, when the step-3 publication is live, title `Autumn 2025/26 results are published`, body `Sign in to your dashboard to see your marks and your average so far.`, and otherwise title `Autumn 2025/26 results are coming`, body `Your dashboard shows a countdown to the moment they are published.`; `expires_at = publish_at + 7 days`. The second: title `Spring 2026/27 enrolment is open`, body `Enrol on spring modules from the catalogue while places last. Each module shows when enrolment closes.`, `expires_at` = the Spring window's `closes_at`. The module one: title `Welcome to Advanced Machine Learning`, body `Welcome to CS3099. The module has 30 places, so enrol early if you have not already.`, no expiry. |
| 11 | `demo_reset_hot_module` | always, demo | `UPDATE enrolments e SET status = 'Withdrawn', withdrawn_at = now(), updated_at = now() WHERE e.module_id = (SELECT id FROM modules WHERE code = 'CS3099') AND e.status = 'Active' AND e.source = 'Self' AND NOT EXISTS (SELECT 1 FROM grades g WHERE g.student_id = e.student_id AND g.module_id = e.module_id)` (the v0 rows relabelled by step 4 and every demo visitor's self-enrolment without a mark; admin overrides and anything a lecturer has marked are kept), then, when `n > 0`, one audit row `system.demo_reset { moduleCode: 'CS3099', withdrawn: n }` with a null actor. The same statement (plus step 13) is the body of the demo-only `POST /api/admin/ops/demo-reset` (`02-api.md` section 8.5). Because the free container restarts on every deploy and after idle spin-down, CS3099's 30 places come back regularly. |
| 12 | `demo_autumn_cohort` | once, demo | Gives the demo lecturer a cohort to mark: `INSERT INTO enrolments (id, student_id, module_id, enrolled_at, status, source, academic_year, withdrawn_at, created_by_user_id, updated_at) SELECT gen_random_uuid(), s.id, m.id, now(), 'Active', 'Seed', @currentYear, NULL, NULL, NULL FROM (SELECT id FROM students WHERE year_of_study = 1 ORDER BY student_number LIMIT 100) s CROSS JOIN modules m WHERE m.code = 'CS3001' AND NOT EXISTS (SELECT 1 FROM enrolments e WHERE e.student_id = s.id AND e.module_id = m.id)` (`@currentYear` from `academic_settings`). Year-1 students never hold level-3 modules from 2025/26, so exactly 100 rows are inserted (`S000001`, `S000004`, …, `S000298`), each now carrying 15 autumn credits for 2026/27. No grades are created: CS3001 shows as `draft`, 0 entered, 100 missing for `L00001`. |
| 13 | `reconcile_enrolled_count` | always, **last** | `UPDATE modules m SET enrolled_count = c.n FROM (SELECT e.module_id, count(*) n FROM enrolments e JOIN academic_settings s ON s.id = 1 AND e.academic_year = s.academic_year WHERE e.status = 'Active' GROUP BY e.module_id) c WHERE m.id = c.module_id AND m.enrolled_count <> c.n;` then `UPDATE modules m SET enrolled_count = 0 WHERE m.enrolled_count <> 0 AND NOT EXISTS (SELECT 1 FROM enrolments e JOIN academic_settings s ON s.id = 1 AND e.academic_year = s.academic_year WHERE e.module_id = m.id AND e.status = 'Active');` Logs any module where `enrolled_count > capacity` at Warning (data-quality item on the ops page; the administrator decides whether to raise capacity, trim to capacity or admin-withdraw). Both run in one transaction after `SELECT id FROM modules ORDER BY id FOR NO KEY UPDATE`, so neither counts from a snapshot older than a commit it waited for (`04-performance-and-ops.md` section 2.3). The lock and the two statements are `StartupBackfills.ReconcileEnrolledCountAsync(db)`, the body of `POST /api/admin/ops/reconcile`, and run inside `PUT /api/admin/settings` (after its settings update) when the academic year changes. It runs last so it sees every demo step's changes. |

**Planner statistics** (review S6 E16): when a start applied a migration or any step wrote rows, `StartupTasks` runs
`StartupBackfills.AnalyzeAsync` last (`ANALYZE users, user_roles, students, lecturers, modules, module_lecturers,
enrolments, grades, results_publications, announcements, enrolment_windows, academic_settings`, a sample of each, about
0.6 s on the demo data), on the owner connection when `ConnectionStrings:Migrations` is set. Without it the planner had
no statistics for the new columns until autovacuum caught up, and the admin results query sorted on disk (180 ms instead
of 56 ms). A role that does not own a table gets a PostgreSQL warning and the table is skipped; any failure is logged
and ignored. A start that changes nothing (the usual restart) does not analyze.

Step 8 insert SQL (parameters bound with Npgsql, never interpolated):

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
is confined to rows with `is_demo = true`; a real deployment never runs step 8, and the first start after demo mode is
switched off runs step 9 instead, which disables every one of them.

Demo-mode guard (`StartupTasks`, before any step): in `Production`, `Demo:Enabled = true` without
`Demo:PublicDemoAcknowledged = true` aborts startup with `Demo mode on a Production deployment requires
Demo__PublicDemoAcknowledged=true`; with it, every start logs a Warning `DEMO MODE: every account has a published
password`.

## 7. Demo accounts

Shown on the login page from `GET /api/public/status` (`demo.accounts`) only when `Demo:Enabled = true`
(set in the Render dashboard for the public demo together with `Demo__PublicDemoAcknowledged`; never in
`render.yaml`; false by default).

| Role | Username | Password | Hint shown on the login page (static; no dates) |
|---|---|---|---|
| Student | `S000001` (any of `S000001`–`S020000` works) | `Student-Demo-2026!` | `Completed Autumn 2025/26 with marks published; enrolled on CS3001 for Autumn 2026/27; can enrol on CS3099` |
| Lecturer | `L00001` | `Lecturer-Demo-2026!` | `Leads CS3001 (100 students, marks in draft) and CS3099 (30 places); enters and submits marks; the admin guide shows how to re-run results day` |
| Administrator | `admin` | `Admin-Demo-2026!` | `Windows, results publication and corrections, accounts, audit, operations` |

The hints carry no date so they are never stale; the login page renders the results line (published or scheduled,
with its instant formatted by the browser) from `latestPublication` / `nextPublication` of `GET /api/public/status`
(`02-api.md` section 8.1, `05-frontend.md` section 10). Constants live in `Infrastructure/Seeding/DemoAccounts.cs`.
Usernames are case-insensitive (`s000001` works). Demo accounts cannot be locked by failed logins, cannot change
their password or enable a second factor, and cannot be locked, disabled or reset by an administrator (409
`demo-account`), so one visitor cannot spoil the demo for the next; a redeploy heals them (step 8), restores the demo
windows and academic year (step 6) and frees CS3099's places (step 11).

## 8. Password policy (Identity options + `RushDayPasswordValidator`)

- `RequiredLength = 12`; maximum 128 characters enforced by request validation; `RequireDigit`, `RequireUppercase`,
  `RequireLowercase`, `RequireNonAlphanumeric` all false; `RequiredUniqueChars = 4`.
- `RushDayPasswordValidator` (an `IPasswordValidator<ApplicationUser>`): rejects when the password contains the
  username (case-insensitive), contains `rushday` (case-insensitive), or is in `BlockedPasswords.Set`, a static
  `FrozenSet<string>` (case-insensitive comparer) loaded from the embedded resource `Identity/blocked-passwords.txt`:
  up to 10,000 of the most common breached passwords of 12 or more characters, one per line, lower-case, generated
  once from SecLists `Passwords/Common-Credentials/10-million-password-list-top-1000000.txt` (MIT) by keeping entries
  of length ≥ 12 in list order, merged with the hand-written entries S1 already has, and committed (about 150 KB).
  Error codes map to 400 `weak-password` with `errors.newPassword[]`. A static
  `RushDayPasswordValidator.Check(string username, string password) → IReadOnlyList<string>` returns the failing codes
  of the whole policy (length 12..128, at least 4 distinct characters, the three rules above) for callers that have no
  `UserManager` (the bootstrap step); `ValidateAsync` uses the same rule methods.
  `POST /api/auth/change-password` additionally rejects a new password equal to the current one with the same slug.
  Should: `IHibpClient` calling the HIBP range API server-side (k-anonymity, no key) with a 2 s timeout that fails open.
- Hasher: Identity default (PBKDF2-HMAC-SHA512, V3 format) with `PasswordHasherOptions.IterationCount = 210000`
  (OWASP's current figure). `Identity/PasswordHashing.cs` holds `public const int IterationCount = 210_000` and
  `Create() → PasswordHasher<ApplicationUser>` built with that option; the backfills use `Create()` and S2's DI
  registration uses the constant, so a demo hash never verifies as `SuccessRehashNeeded` (which would make
  `PasswordSignInAsync` rewrite a per-user hash on first login). `login-storm.js` measures the cost, and the login
  concurrency guard exists for it.
- Temporary passwords (provision, reset): 16 characters drawn with `RandomNumberGenerator` from the 57-character
  alphabet `A-Z a-z 2-9` minus `I l O` (no `0`, `1`, `I`, `l`, `O`; about 93 bits), shown once.
- `Bootstrap:AdminPassword` is run through `RushDayPasswordValidator.Check` before use (section 6 step 5).
- Lockout: `AllowedForNewUsers = true`, `MaxFailedAccessAttempts = 5`, `DefaultLockoutTimeSpan = 15 minutes`; when
  failures count is decided by `LoginThrottle` (`02-api.md` section 2.3): never for `is_demo` users, and only when the
  account's recent failures come from at least three distinct addresses.

## 9. Local verification without Docker

Prerequisites: PostgreSQL 18 on `localhost:5432` with the seeded `rushday` database (`scripts/seed.ps1`); the API is
run only through `scripts/run-api.ps1` (`06-implementation-plan.md` section 1 rule 5, owned by stage S1R), never `dotnet run`.

1. Stop any running API (`CREATE DATABASE ... TEMPLATE rushday` requires no other session on `rushday`), then clone
   the seeded database for a rehearsal (PowerShell, `psql` on PATH):
   `psql -U postgres -h localhost -c "DROP DATABASE IF EXISTS rushday_migtest WITH (FORCE)" -c "CREATE DATABASE rushday_migtest TEMPLATE rushday OWNER rushday"`.
2. Run the startup path against it twice:
   `$env:ConnectionStrings__RushDay="Host=localhost;Database=rushday_migtest;Username=rushday;Password=rushday"; $env:Demo__Enabled="true"; scripts/run-api.ps1 -Args "--migrate-and-seed"` (twice).
3. Assert in `psql -d rushday_migtest`:
   - `SELECT count(*) FROM users;` = 20,041 (20,000 students + 40 lecturers + admin) after both runs.
   - `SELECT count(*) FROM data_backfills;` = 12 with demo on (steps 1–8 and 10–13), 7 with demo off (steps 1–5, 9 and
     13; step 5 always writes its row, with `rows_affected = 0` and `notes = 'skipped: no password'` when it created
     nothing).
   - `SELECT code, capacity, enrolled_count, (SELECT count(*) FROM enrolments e WHERE e.module_id = m.id AND e.status = 'Active' AND e.academic_year = '2026/27') FROM modules m WHERE code IN ('CS3099', 'CS3001');`
     shows CS3099 `30, 0, 0` (the 154 v0 rows are `Withdrawn`, `source = 'Self'`, `academic_year = '2026/27'`) and
     CS3001 `1500, 100, 100` (its ~1,333 completed 2025/26 enrolments stay `Active` but take no 2026/27 place).
   - `SELECT status, outcome, count(*) FROM grades GROUP BY status, outcome;` = `Published Mark 80000`; `SELECT count(*) FROM grades WHERE publication_id IS NULL;` = 0;
     `SELECT academic_year, count(*) FROM enrolments GROUP BY academic_year;` = `2025/26 80000`, `2026/27 254` locally
     (154 withdrawn v0 rows + 100 CS3001 rows; `2026/27 100` on a database rebuilt by `scripts/reset-db.ps1`).
   - `SELECT count(*) FROM audit_events WHERE action = 'system.demo_reset';` = 1 after both runs (the second run
     withdraws nothing, so it writes no row); `SELECT current_semester FROM academic_settings;` = 1.
   - `SELECT count(*) FROM module_lecturers WHERE role = 'Leader';` = 121; `SELECT l.staff_number FROM module_lecturers ml JOIN lecturers l ON l.id = ml.lecturer_id JOIN modules m ON m.id = ml.module_id WHERE m.code = 'CS3099' AND ml.role = 'Leader';` = `L00001`; the same query with `ml.role = 'Teacher'` = `L00006`; with `m.code = 'CS3001' AND ml.role = 'Leader'` = `L00001`.
   - `UPDATE audit_events SET action = 'x' WHERE false;` succeeds (no rows) but `DELETE FROM audit_events WHERE id = (SELECT id FROM audit_events LIMIT 1);` fails with `audit_events is append-only`.
4. `dotnet ef migrations script --idempotent -p src/RushDay.Infrastructure -s src/RushDay.Api -o $env:TEMP/rushday.sql`
   produces a script with no errors; review it for the DROP DEFAULT statements (`department`, `academic_year`,
   `grades.status`, `grades.updated_at`; **not** `source`), the trigger, and the absence of any `enrolled_count`
   reconciliation, and for the `ResultsGovernance` part (section 5a: the column, the leader demotion ending in `;`,
   the two indexes, the foreign key). (`dotnet ef` runs through `dotnet exec` and works under Smart App Control; if it
   is ever blocked, as it was for a freshly built Debug `RushDay.Domain.dll` on 2026-09-29, run it with `--no-build
   --configuration Release`.) Known limitation, not changed because `PortalAndIdentity` stays as rehearsed: applied
   through `psql`, the script stops at `PortalAndIdentity`'s `Sql("UPDATE modules SET department = left(code, 2)")`
   block: that statement and the four `... DROP DEFAULT` ones (`department`, `academic_year`, `grades.status`,
   `grades.updated_at`) have no terminating semicolon, so each generated `DO` block's `END IF` is a syntax error.
   Every deployment migrates through `MigrateAsync`, which is unaffected; a DBA who applies the script by hand adds
   those five semicolons first.
5. `scripts/reset-db.ps1` (drop, recreate, `run-api.ps1 -Args "--migrate-and-seed"`) still converges to identical data
   from empty.
6. Integration test `Persistence/MigrationOnSeededDatabaseTests` (stage S11) automates 1–3: applies `InitialCreate`,
   seeds 300 students with `DatabaseSeeder`, applies `PortalAndIdentity`, runs `StartupBackfills` twice with demo on,
   asserts equal counts and that every step of the second run affected zero rows; a further case
   (`Demo_off_disables_every_demo_account`) runs the backfills once more with `DemoEnabled = false` and asserts every
   `is_demo` row has `disabled_at IS NOT NULL` and a changed `security_stamp`, then once more with demo on and asserts
   they are enabled again; `Bootstrap_password_rejected_by_policy_is_not_used` runs demo off with
   `BootstrapAdminPassword = "password1234"` and asserts no administrator was created and the step's notes name the
   policy codes; `Enrolled_count_is_scoped_to_the_current_year` changes `academic_settings.academic_year` and asserts
   the reconciliation zeroes modules that only have earlier-year enrolments.
