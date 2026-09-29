using System.Diagnostics;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;
using RushDay.Domain.Announcements;
using RushDay.Domain.Audit;
using RushDay.Domain.Enrolments;
using RushDay.Domain.Grades;
using RushDay.Domain.Modules;
using RushDay.Domain.Results;
using RushDay.Domain.Settings;
using RushDay.Domain.Users;
using RushDay.Infrastructure.Identity;
using RushDay.Infrastructure.Persistence;

namespace RushDay.Infrastructure.Seeding;

/// <summary>What one backfill step did on this start.</summary>
public sealed record BackfillResult(string Name, int RowsAffected, bool Skipped, TimeSpan Elapsed, string? Notes);

/// <summary>
/// The idempotent startup steps of 01-domain-and-data.md section 6, in its order. They run after the migration and
/// the seeder, against a database that may already hold 20,000 students. Steps marked "always" run on every start;
/// "once" steps are skipped when their <c>data_backfills</c> row exists; "demo" steps run only with
/// <c>Demo:Enabled</c> and "demo off" steps only without it. Each step commits its own transaction together with
/// its <c>data_backfills</c> row, so a crash between steps resumes at the failed step on the next start.
/// </summary>
public static class StartupBackfills
{
    public static class StepNames
    {
        public const string Roles = "roles";
        public const string AcademicSettings = "academic_settings";
        public const string ResultsPublication = "results_publication_autumn_2025_26";
        public const string RelabelV0SelfEnrolments = "relabel_v0_self_enrolments";
        public const string AdminAccount = "admin_account";
        public const string EnrolmentWindows = "enrolment_windows_2026_27";
        public const string LecturersAndAssignments = "lecturers_and_assignments";
        public const string DemoAccounts = "demo_accounts";
        public const string DemoAccountsDisable = "demo_accounts_disable";
        public const string DemoAnnouncements = "demo_announcements";
        public const string DemoResetHotModule = "demo_reset_hot_module";
        public const string DemoAutumnCohort = "demo_autumn_cohort";
        public const string ReconcileEnrolledCount = "reconcile_enrolled_count";
    }

    public const string CurrentAcademicYear = "2026/27";
    public const string SeededResultsAcademicYear = DatabaseSeeder.SeedAcademicYear;
    public const string SeededResultsNote = "Seeded autumn results";
    public const string DemoCohortModuleCode = "CS3001";
    public const int DemoCohortSize = 100;

    public const string AdminSkippedNote = "skipped: no password";
    public const string AdminUsernameTakenNote = "skipped: username taken";
    public const string AdminPasswordRejectedNote = "skipped: password rejected by policy";

    /// <summary>
    /// Year-scoped reconciliation of modules.enrolled_count (D28): active enrolments in academic_settings.academic_year.
    /// Also the body of the ops reconcile action and of a settings change of the academic year.
    /// </summary>
    public const string ReconcileEnrolledCountSql =
        "UPDATE modules m SET enrolled_count = c.n FROM (SELECT e.module_id, count(*) n FROM enrolments e JOIN academic_settings s ON s.id = 1 AND e.academic_year = s.academic_year WHERE e.status = 'Active' GROUP BY e.module_id) c WHERE m.id = c.module_id AND m.enrolled_count <> c.n;";

    /// <summary>The first statement of the reconciliation: every module row, in id order, before either count is taken.</summary>
    public const string ReconcileLockModulesSql = "SELECT id FROM modules ORDER BY id FOR NO KEY UPDATE;";

    public const string ReconcileZeroEnrolledCountSql =
        "UPDATE modules m SET enrolled_count = 0 WHERE m.enrolled_count <> 0 AND NOT EXISTS (SELECT 1 FROM enrolments e JOIN academic_settings s ON s.id = 1 AND e.academic_year = s.academic_year WHERE e.module_id = m.id AND e.status = 'Active');";

    private const string DemoStudentUsersSql = """
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
        """;

    private const string DemoStudentRolesSql = """
        INSERT INTO user_roles (user_id, role_id)
        SELECT u.id, @studentRoleId FROM users u
        WHERE u.student_id IS NOT NULL AND NOT EXISTS (SELECT 1 FROM user_roles ur WHERE ur.user_id = u.id);
        """;

    private const string DemoLecturerUsersSql = """
        INSERT INTO users (id, user_name, normalized_user_name, email, normalized_email, email_confirmed,
                           password_hash, security_stamp, concurrency_stamp, phone_number, phone_number_confirmed,
                           two_factor_enabled, lockout_end, lockout_enabled, access_failed_count,
                           display_name, student_id, lecturer_id, must_change_password, is_demo, created_at, disabled_at, last_login_at)
        SELECT gen_random_uuid(), l.staff_number, upper(l.staff_number), NULL, NULL, false,
               @lecturerHash, upper(replace(gen_random_uuid()::text, '-', '')), gen_random_uuid()::text, NULL, false,
               false, NULL, true, 0,
               l.full_name, NULL, l.id, false, true, now(), NULL, NULL
        FROM lecturers l
        WHERE NOT EXISTS (SELECT 1 FROM users u WHERE u.lecturer_id = l.id);
        """;

    private const string DemoLecturerRolesSql = """
        INSERT INTO user_roles (user_id, role_id)
        SELECT u.id, @lecturerRoleId FROM users u
        WHERE u.lecturer_id IS NOT NULL AND NOT EXISTS (SELECT 1 FROM user_roles ur WHERE ur.user_id = u.id);
        """;

    private const string RewriteDemoStudentHashSql =
        "UPDATE users SET password_hash = @hash WHERE is_demo AND student_id IS NOT NULL";

    private const string RewriteDemoLecturerHashSql =
        "UPDATE users SET password_hash = @hash WHERE is_demo AND lecturer_id IS NOT NULL";

    private const string RewriteDemoAdminHashSql =
        "UPDATE users SET password_hash = @hash WHERE is_demo AND EXISTS (SELECT 1 FROM user_roles ur WHERE ur.user_id = users.id AND ur.role_id = @adminRoleId)";

    /// <summary>Repairs every demo login a visitor may have spoiled; a redeploy of the public demo heals it.</summary>
    private const string HealDemoUsersSql = """
        UPDATE users SET disabled_at = NULL, lockout_end = NULL, access_failed_count = 0, must_change_password = false, two_factor_enabled = false,
                         security_stamp = CASE WHEN disabled_at IS NOT NULL THEN upper(replace(gen_random_uuid()::text, '-', '')) ELSE security_stamp END
        WHERE is_demo AND (disabled_at IS NOT NULL OR lockout_end IS NOT NULL OR access_failed_count <> 0 OR must_change_password OR two_factor_enabled);
        """;

    private const string DeleteDemoUserTokensSql =
        "DELETE FROM user_tokens WHERE user_id IN (SELECT id FROM users WHERE is_demo)";

    private const string DisableDemoUsersSql =
        "UPDATE users SET disabled_at = now(), lockout_end = NULL, security_stamp = upper(replace(gen_random_uuid()::text, '-', '')) WHERE is_demo AND disabled_at IS NULL";

    private const string DemoCohortSql = """
        INSERT INTO enrolments (id, student_id, module_id, enrolled_at, status, source, academic_year, withdrawn_at, created_by_user_id, updated_at)
        SELECT gen_random_uuid(), s.id, m.id, now(), 'Active', 'Seed', @currentYear, NULL, NULL, NULL
        FROM (SELECT id FROM students WHERE year_of_study = 1 ORDER BY student_number LIMIT @cohortSize) s
        CROSS JOIN modules m
        WHERE m.code = @moduleCode AND NOT EXISTS (SELECT 1 FROM enrolments e WHERE e.student_id = s.id AND e.module_id = m.id);
        """;

    private static readonly DateTimeOffset V0SelfEnrolmentCutoff = new(2026, 9, 1, 0, 0, 0, TimeSpan.Zero);

    private static readonly JsonSerializerOptions DetailsJson = new(JsonSerializerDefaults.Web);

    public static async Task<IReadOnlyList<BackfillResult>> RunAsync(
        RushDayDbContext db,
        StartupBackfillOptions options,
        TimeProvider clock,
        ILogger logger,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(logger);

        var results = new List<BackfillResult>(13)
        {
            await RunStepAsync(db, StepNames.Roles, once: false, ct => InsertRolesAsync(db, ct), clock, logger, cancellationToken),
            await RunStepAsync(db, StepNames.AcademicSettings, once: true, ct => InsertAcademicSettingsAsync(db, options, clock, ct), clock, logger, cancellationToken),
            await RunStepAsync(db, StepNames.ResultsPublication, once: true, ct => InsertSeedPublicationAsync(db, options, clock, ct), clock, logger, cancellationToken),
            await RunStepAsync(db, StepNames.RelabelV0SelfEnrolments, once: true, ct => RelabelV0SelfEnrolmentsAsync(db, ct), clock, logger, cancellationToken),
            await RunStepAsync(db, StepNames.AdminAccount, once: false, ct => EnsureAdminAccountAsync(db, options, clock, logger, ct), clock, logger, cancellationToken),
        };

        if (options.DemoEnabled)
        {
            results.Add(await RunStepAsync(db, StepNames.EnrolmentWindows, once: false, ct => UpsertEnrolmentWindowsAsync(db, ct), clock, logger, cancellationToken));
            results.Add(await RunStepAsync(db, StepNames.LecturersAndAssignments, once: true, ct => InsertLecturersAsync(db, clock, ct), clock, logger, cancellationToken));
            results.Add(await RunStepAsync(db, StepNames.DemoAccounts, once: false, ct => EnsureDemoAccountsAsync(db, ct), clock, logger, cancellationToken));
            results.Add(await RunStepAsync(db, StepNames.DemoAnnouncements, once: true, ct => InsertDemoAnnouncementsAsync(db, clock, ct), clock, logger, cancellationToken));
            results.Add(await RunStepAsync(db, StepNames.DemoResetHotModule, once: false, ct => ResetDemoHotModuleAsync(db, clock, ct), clock, logger, cancellationToken));
            results.Add(await RunStepAsync(db, StepNames.DemoAutumnCohort, once: true, ct => InsertDemoAutumnCohortAsync(db, ct), clock, logger, cancellationToken));
        }
        else
        {
            logger.LogInformation("Demo backfills not run: demo mode is off.");
            results.Add(await RunStepAsync(db, StepNames.DemoAccountsDisable, once: false, ct => DisableDemoAccountsAsync(db, clock, logger, ct), clock, logger, cancellationToken));
        }

        results.Add(await RunStepAsync(db, StepNames.ReconcileEnrolledCount, once: false, ct => ReconcileEnrolledCountAsync(db, logger, ct), clock, logger, cancellationToken));

        return results;
    }

    /// <summary>
    /// Runs the two year-scoped reconciliation statements in one transaction (the caller's when there is one, as in the
    /// settings year change and the startup step; else its own) and returns the number of module rows corrected. Every
    /// module row is locked first (<see cref="ReconcileLockModulesSql"/>, id order, the mode an enrolment's claim takes),
    /// so the lock waits out any enrolment or withdrawal that has already changed a count, and the two
    /// <c>UPDATE</c>s then run as fresh statements whose snapshots include that transaction's rows. Without the lock the
    /// counting subquery's snapshot predates the commit the <c>UPDATE</c> waited for, and the stale count is written over
    /// the committed one (04-performance-and-ops.md section 2.3).
    /// </summary>
    public static async Task<int> ReconcileEnrolledCountAsync(RushDayDbContext db, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(db);

        var own = db.Database.CurrentTransaction is null
            ? await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.ReadCommitted, cancellationToken)
            : null;
        try
        {
            await db.Database.ExecuteSqlRawAsync(ReconcileLockModulesSql, cancellationToken);
            var corrected = await db.Database.ExecuteSqlRawAsync(ReconcileEnrolledCountSql, cancellationToken);
            corrected += await db.Database.ExecuteSqlRawAsync(ReconcileZeroEnrolledCountSql, cancellationToken);
            if (own is not null)
            {
                await own.CommitAsync(cancellationToken);
            }

            return corrected;
        }
        finally
        {
            if (own is not null)
            {
                await own.DisposeAsync();
            }
        }
    }

    /// <summary>
    /// Withdraws every active self-enrolment on CS3099 that has no grade (step 11); also the body of the demo-only
    /// ops reset. Admin overrides and anything a lecturer has marked are kept.
    /// </summary>
    public static Task<int> WithdrawDemoHotModuleEnrolmentsAsync(RushDayDbContext db, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(db);

        return db.Database.ExecuteSqlAsync(
            $"""
            UPDATE enrolments e SET status = 'Withdrawn', withdrawn_at = now(), updated_at = now()
            WHERE e.module_id = (SELECT id FROM modules WHERE code = {DatabaseSeeder.HotModuleCode})
              AND e.status = 'Active' AND e.source = 'Self'
              AND NOT EXISTS (SELECT 1 FROM grades g WHERE g.student_id = e.student_id AND g.module_id = e.module_id)
            """,
            cancellationToken);
    }

    private static async Task<BackfillResult> RunStepAsync(
        RushDayDbContext db,
        string name,
        bool once,
        Func<CancellationToken, Task<StepOutcome>> step,
        TimeProvider clock,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();

        if (once && await db.DataBackfills.AsNoTracking().AnyAsync(b => b.Name == name, cancellationToken))
        {
            logger.LogInformation("Backfill {Backfill}: already completed, skipped.", name);
            return new BackfillResult(name, 0, Skipped: true, stopwatch.Elapsed, "already completed");
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var outcome = await step(cancellationToken);
        await UpsertRowAsync(db, name, clock.GetUtcNow(), outcome.RowsAffected, outcome.Notes, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        db.ChangeTracker.Clear();

        stopwatch.Stop();
        logger.LogInformation(
            "Backfill {Backfill}: {RowsAffected} rows affected in {ElapsedMs} ms ({Notes}).",
            name,
            outcome.RowsAffected,
            stopwatch.ElapsedMilliseconds,
            outcome.Notes ?? "ok");

        return new BackfillResult(name, outcome.RowsAffected, Skipped: false, stopwatch.Elapsed, outcome.Notes);
    }

    private static Task<int> UpsertRowAsync(RushDayDbContext db, string name, DateTimeOffset completedAt, int rowsAffected, string? notes, CancellationToken cancellationToken) =>
        db.Database.ExecuteSqlAsync(
            $"""
            INSERT INTO data_backfills (name, completed_at, rows_affected, notes)
            VALUES ({name}, {completedAt}, {rowsAffected}, {notes})
            ON CONFLICT (name) DO UPDATE SET completed_at = EXCLUDED.completed_at, rows_affected = EXCLUDED.rows_affected, notes = EXCLUDED.notes
            """,
            cancellationToken);

    /// <summary>Audit rows written by backfills have a null actor and no request; they commit with the step (03-security.md section 7).</summary>
    private static void Audit(RushDayDbContext db, TimeProvider clock, string action, string? subjectId, object details, Guid? moduleId = null)
    {
        db.AuditEvents.Add(new AuditEvent
        {
            Id = Guid.CreateVersion7(),
            OccurredAt = clock.GetUtcNow(),
            ActorUserId = null,
            ActorUsername = null,
            ActorRole = null,
            Action = action,
            SubjectType = AuditActions.SubjectOf(action),
            SubjectId = subjectId,
            ModuleId = moduleId,
            Details = JsonSerializer.Serialize(details, DetailsJson),
        });
    }

    // 1. roles (always)
    private static async Task<StepOutcome> InsertRolesAsync(RushDayDbContext db, CancellationToken cancellationToken)
    {
        var inserted = 0;
        foreach (var role in RushDayRoles.All)
        {
            var id = RushDayRoles.IdOf(role);
            var normalizedName = role.ToUpperInvariant();
            var concurrencyStamp = Guid.NewGuid().ToString();

            inserted += await db.Database.ExecuteSqlAsync(
                $"INSERT INTO roles (id, name, normalized_name, concurrency_stamp) VALUES ({id}, {role}, {normalizedName}, {concurrencyStamp}) ON CONFLICT (normalized_name) DO NOTHING",
                cancellationToken);
        }

        return new StepOutcome(inserted, null);
    }

    // 2. academic_settings (once)
    private static async Task<StepOutcome> InsertAcademicSettingsAsync(RushDayDbContext db, StartupBackfillOptions options, TimeProvider clock, CancellationToken cancellationToken)
    {
        if (await db.AcademicSettings.AnyAsync(s => s.Id == AcademicSettings.SingletonId, cancellationToken))
        {
            return new StepOutcome(0, "exists");
        }

        db.AcademicSettings.Add(new AcademicSettings
        {
            Id = AcademicSettings.SingletonId,
            AcademicYear = CurrentAcademicYear,
            InstitutionName = options.InstitutionName,
            InstitutionShortName = options.InstitutionShortName,
            TimeZone = options.TimeZone,
            CurrentSemester = Semester.Autumn,
            SupportEmail = null,
            SupportUrl = null,
            UpdatedAt = clock.GetUtcNow(),
        });
        await db.SaveChangesAsync(cancellationToken);

        return new StepOutcome(1, null);
    }

    // 3. results_publication_autumn_2025_26 (once)
    private static async Task<StepOutcome> InsertSeedPublicationAsync(RushDayDbContext db, StartupBackfillOptions options, TimeProvider clock, CancellationToken cancellationToken)
    {
        if (await db.ResultsPublications.AnyAsync(p => p.AcademicYear == SeededResultsAcademicYear && p.Semester == Semester.Autumn, cancellationToken))
        {
            return new StepOutcome(0, "exists");
        }

        var pending = db.Grades.Where(g => g.PublicationId == null && g.Status == GradeStatus.Published);
        var gradeCount = await pending.CountAsync(cancellationToken);
        if (gradeCount == 0)
        {
            // A customer database has no seeded results to attach.
            return new StepOutcome(0, "skipped: no unattached published grades");
        }

        var moduleCount = await pending.Select(g => g.ModuleId).Distinct().CountAsync(cancellationToken);

        var publication = new ResultsPublication
        {
            Id = Guid.CreateVersion7(),
            AcademicYear = SeededResultsAcademicYear,
            Semester = Semester.Autumn,
            PublishAt = options.SeedResultsDay,
            CreatedAt = clock.GetUtcNow(),
            CreatedByUserId = null,
            GradeCount = gradeCount,
            ModuleCount = moduleCount,
            Note = SeededResultsNote,
        };
        db.ResultsPublications.Add(publication);
        await db.SaveChangesAsync(cancellationToken);

        var publicationId = publication.Id;
        var attached = await db.Database.ExecuteSqlAsync(
            $"UPDATE grades SET publication_id = {publicationId} WHERE publication_id IS NULL AND status = 'Published'",
            cancellationToken);

        return new StepOutcome(1 + attached, $"{attached} grades attached across {moduleCount} modules");
    }

    // 4. relabel_v0_self_enrolments (once): the v0 load-run and demo-visitor rows on CS3099 were made during the
    //    2026/27 window by students, not by the seed (seed rows are dated September 2025).
    private static async Task<StepOutcome> RelabelV0SelfEnrolmentsAsync(RushDayDbContext db, CancellationToken cancellationToken)
    {
        var relabelled = await db.Database.ExecuteSqlAsync(
            $"""
            UPDATE enrolments SET source = 'Self', academic_year = {CurrentAcademicYear}
            WHERE source = 'Seed' AND enrolled_at >= {V0SelfEnrolmentCutoff}
              AND module_id = (SELECT id FROM modules WHERE code = {DatabaseSeeder.HotModuleCode})
            """,
            cancellationToken);

        return new StepOutcome(relabelled, null);
    }

    // 5. admin_account (always): create the bootstrap administrator when no usable one exists.
    private static async Task<StepOutcome> EnsureAdminAccountAsync(RushDayDbContext db, StartupBackfillOptions options, TimeProvider clock, ILogger logger, CancellationToken cancellationToken)
    {
        var demoEnabled = options.DemoEnabled;
        var adminRoleId = RushDayRoles.AdminId;
        var usableAdminExists = await db.Users.AsNoTracking()
            .Where(u => u.DisabledAt == null && (!u.IsDemo || demoEnabled))
            .Where(u => db.UserRoles.Any(ur => ur.UserId == u.Id && ur.RoleId == adminRoleId))
            .AnyAsync(cancellationToken);
        if (usableAdminExists)
        {
            return new StepOutcome(0, "exists");
        }

        var username = options.BootstrapAdminUsername;
        var normalizedUsername = username.ToUpperInvariant();
        if (await db.Users.AsNoTracking().AnyAsync(u => u.NormalizedUserName == normalizedUsername, cancellationToken))
        {
            logger.LogWarning("Bootstrap username '{Username}' is taken; set Bootstrap__AdminUsername to another name", username);
            return new StepOutcome(0, AdminUsernameTakenNote);
        }

        string password;
        bool mustChangePassword;
        bool isDemo;
        if (!string.IsNullOrEmpty(options.BootstrapAdminPassword))
        {
            var codes = RushDayPasswordValidator.Check(username, options.BootstrapAdminPassword);
            if (codes.Count > 0)
            {
                logger.LogWarning("Bootstrap password rejected by policy: {Codes}", string.Join(", ", codes));
                return new StepOutcome(0, AdminPasswordRejectedNote);
            }

            password = options.BootstrapAdminPassword;
            mustChangePassword = true;
            isDemo = false;
        }
        else if (options.DemoEnabled)
        {
            password = DemoAccounts.AdminPassword;
            mustChangePassword = false;
            isDemo = true;
        }
        else
        {
            logger.LogWarning("No administrator account exists; set Bootstrap__AdminPassword and restart.");
            return new StepOutcome(0, AdminSkippedNote);
        }

        var admin = new ApplicationUser
        {
            Id = Guid.CreateVersion7(),
            UserName = username,
            NormalizedUserName = normalizedUsername,
            DisplayName = isDemo ? DemoAccounts.AdminDisplayName : DemoAccounts.NonDemoAdminDisplayName,
            MustChangePassword = mustChangePassword,
            IsDemo = isDemo,
            LockoutEnabled = true,
            SecurityStamp = NewSecurityStamp(),
            ConcurrencyStamp = Guid.NewGuid().ToString(),
            CreatedAt = clock.GetUtcNow(),
        };
        admin.PasswordHash = PasswordHashing.Create().HashPassword(admin, password);

        db.Users.Add(admin);
        db.UserRoles.Add(new IdentityUserRole<Guid> { UserId = admin.Id, RoleId = RushDayRoles.AdminId });
        Audit(db, clock, AuditActions.AccountProvisioned, admin.Id.ToString(), new { username, role = RushDayRoles.Admin });
        await db.SaveChangesAsync(cancellationToken);

        return new StepOutcome(1, mustChangePassword ? "created with bootstrap password; must change" : "created with demo password");
    }

    // 6. enrolment_windows_2026_27 (always, demo): keeps the demo's two windows and academic year in place.
    private static async Task<StepOutcome> UpsertEnrolmentWindowsAsync(RushDayDbContext db, CancellationToken cancellationToken)
    {
        var rows = await UpsertWindowAsync(
            db,
            Semester.Autumn,
            new DateTimeOffset(2026, 9, 14, 9, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 10, 2, 17, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 10, 30, 17, 0, 0, TimeSpan.Zero),
            cancellationToken);
        rows += await UpsertWindowAsync(
            db,
            Semester.Spring,
            new DateTimeOffset(2026, 9, 14, 9, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2027, 1, 29, 17, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2027, 2, 26, 17, 0, 0, TimeSpan.Zero),
            cancellationToken);

        var healedYear = await db.Database.ExecuteSqlAsync(
            $"UPDATE academic_settings SET academic_year = {CurrentAcademicYear}, updated_at = now() WHERE id = 1 AND academic_year <> {CurrentAcademicYear}",
            cancellationToken);

        return new StepOutcome(rows + healedYear, healedYear > 0 ? "academic year restored" : null);
    }

    private static Task<int> UpsertWindowAsync(RushDayDbContext db, Semester semester, DateTimeOffset opensAt, DateTimeOffset closesAt, DateTimeOffset withdrawalDeadlineAt, CancellationToken cancellationToken)
    {
        var semesterNumber = (int)semester;
        return db.Database.ExecuteSqlAsync(
            $"""
            INSERT INTO enrolment_windows (id, academic_year, semester, opens_at, closes_at, withdrawal_deadline_at, created_by_user_id, updated_at)
            VALUES (gen_random_uuid(), {CurrentAcademicYear}, {semesterNumber}, {opensAt}, {closesAt}, {withdrawalDeadlineAt}, NULL, now())
            ON CONFLICT (academic_year, semester) DO UPDATE
            SET opens_at = EXCLUDED.opens_at, closes_at = EXCLUDED.closes_at, withdrawal_deadline_at = EXCLUDED.withdrawal_deadline_at, updated_at = now()
            WHERE (enrolment_windows.opens_at, enrolment_windows.closes_at, enrolment_windows.withdrawal_deadline_at)
                  IS DISTINCT FROM (EXCLUDED.opens_at, EXCLUDED.closes_at, EXCLUDED.withdrawal_deadline_at)
            """,
            cancellationToken);
    }

    // 7. lecturers_and_assignments (once, demo)
    private static async Task<StepOutcome> InsertLecturersAsync(RushDayDbContext db, TimeProvider clock, CancellationToken cancellationToken)
    {
        if (await db.Lecturers.AnyAsync(cancellationToken))
        {
            return new StepOutcome(0, "exists");
        }

        var modules = await db.Modules.AsNoTracking().OrderBy(m => m.Code).ToListAsync(cancellationToken);
        var lecturers = LecturerSeed.BuildLecturers();
        var assignments = LecturerSeed.BuildAssignments(modules, lecturers, clock.GetUtcNow());

        db.Lecturers.AddRange(lecturers);
        db.ModuleLecturers.AddRange(assignments);
        await db.SaveChangesAsync(cancellationToken);

        return new StepOutcome(lecturers.Count + assignments.Count, $"{lecturers.Count} lecturers, {assignments.Count} assignments");
    }

    // 8. demo_accounts (always, demo): one hash per role (hashing 20,000 passwords on 0.1 CPU would take hours),
    //    reused while it still verifies; set-based inserts; then the heal so a redeploy repairs the public demo.
    private static async Task<StepOutcome> EnsureDemoAccountsAsync(RushDayDbContext db, CancellationToken cancellationToken)
    {
        var hasher = PasswordHashing.Create();
        var adminRoleId = RushDayRoles.AdminId;

        var storedStudentHash = await StoredHashAsync(db, DemoAccounts.StudentUsername, cancellationToken);
        var storedLecturerHash = await StoredHashAsync(db, DemoAccounts.LecturerUsername, cancellationToken);
        var storedAdminHash = await db.Users.AsNoTracking()
            .Where(u => u.IsDemo && db.UserRoles.Any(ur => ur.UserId == u.Id && ur.RoleId == adminRoleId))
            .OrderBy(u => u.CreatedAt)
            .Select(u => u.PasswordHash)
            .FirstOrDefaultAsync(cancellationToken);

        var rows = 0;
        var (studentHash, studentRewrites) = await EnsureRoleHashAsync(db, hasher, storedStudentHash, DemoAccounts.StudentPassword, RewriteDemoStudentHashSql, [], cancellationToken);
        var (lecturerHash, lecturerRewrites) = await EnsureRoleHashAsync(db, hasher, storedLecturerHash, DemoAccounts.LecturerPassword, RewriteDemoLecturerHashSql, [], cancellationToken);
        var (_, adminRewrites) = await EnsureRoleHashAsync(db, hasher, storedAdminHash, DemoAccounts.AdminPassword, RewriteDemoAdminHashSql, [new NpgsqlParameter("adminRoleId", adminRoleId)], cancellationToken);
        rows += studentRewrites + lecturerRewrites + adminRewrites;

        rows += await db.Database.ExecuteSqlRawAsync(DemoStudentUsersSql, [new NpgsqlParameter("studentHash", studentHash)], cancellationToken);
        rows += await db.Database.ExecuteSqlRawAsync(DemoStudentRolesSql, [new NpgsqlParameter("studentRoleId", RushDayRoles.StudentId)], cancellationToken);
        rows += await db.Database.ExecuteSqlRawAsync(DemoLecturerUsersSql, [new NpgsqlParameter("lecturerHash", lecturerHash)], cancellationToken);
        rows += await db.Database.ExecuteSqlRawAsync(DemoLecturerRolesSql, [new NpgsqlParameter("lecturerRoleId", RushDayRoles.LecturerId)], cancellationToken);

        var healed = await db.Database.ExecuteSqlRawAsync(HealDemoUsersSql, cancellationToken);
        var tokensDeleted = await db.Database.ExecuteSqlRawAsync(DeleteDemoUserTokensSql, cancellationToken);
        rows += healed + tokensDeleted;

        var rewritten = studentRewrites + lecturerRewrites + adminRewrites;
        string? notes = rewritten == 0 && healed == 0 && tokensDeleted == 0
            ? null
            : $"{rewritten} hashes rewritten, {healed} accounts healed, {tokensDeleted} tokens deleted";
        return new StepOutcome(rows, notes);
    }

    private static Task<string?> StoredHashAsync(RushDayDbContext db, string username, CancellationToken cancellationToken)
    {
        var normalized = username.ToUpperInvariant();
        return db.Users.AsNoTracking()
            .Where(u => u.NormalizedUserName == normalized && u.IsDemo)
            .Select(u => u.PasswordHash)
            .FirstOrDefaultAsync(cancellationToken);
    }

    /// <summary>Reuses a stored hash that verifies as Success; otherwise hashes afresh and rewrites the role's demo rows.</summary>
    private static async Task<(string Hash, int Rewritten)> EnsureRoleHashAsync(
        RushDayDbContext db,
        PasswordHasher<ApplicationUser> hasher,
        string? storedHash,
        string password,
        string rewriteSql,
        NpgsqlParameter[] extraParameters,
        CancellationToken cancellationToken)
    {
        var probe = new ApplicationUser();
        if (storedHash is not null && hasher.VerifyHashedPassword(probe, storedHash, password) == PasswordVerificationResult.Success)
        {
            return (storedHash, 0);
        }

        var hash = hasher.HashPassword(probe, password);
        object[] parameters = [new NpgsqlParameter("hash", hash), .. extraParameters];
        var rewritten = await db.Database.ExecuteSqlRawAsync(rewriteSql, parameters, cancellationToken);
        return (hash, rewritten);
    }

    // 9. demo_accounts_disable (always, demo off): the first start after demo mode is switched off.
    private static async Task<StepOutcome> DisableDemoAccountsAsync(RushDayDbContext db, TimeProvider clock, ILogger logger, CancellationToken cancellationToken)
    {
        var disabled = await db.Database.ExecuteSqlRawAsync(DisableDemoUsersSql, cancellationToken);
        if (disabled == 0)
        {
            return new StepOutcome(0, null);
        }

        logger.LogWarning("Disabled {Count} demo accounts because Demo:Enabled is false", disabled);
        Audit(db, clock, AuditActions.SystemDemoAccountsDisabled, null, new { count = disabled });
        await db.SaveChangesAsync(cancellationToken);

        return new StepOutcome(disabled, $"{disabled} demo accounts disabled");
    }

    // 10. demo_announcements (once, demo): the text never contains a formatted date (D26).
    private static async Task<StepOutcome> InsertDemoAnnouncementsAsync(RushDayDbContext db, TimeProvider clock, CancellationToken cancellationToken)
    {
        var adminRoleId = RushDayRoles.AdminId;
        var adminId = await db.Users.AsNoTracking()
            .Where(u => db.UserRoles.Any(ur => ur.UserId == u.Id && ur.RoleId == adminRoleId))
            .OrderByDescending(u => u.IsDemo)
            .ThenBy(u => u.CreatedAt)
            .Select(u => (Guid?)u.Id)
            .FirstOrDefaultAsync(cancellationToken);

        var lecturerNormalized = DemoAccounts.LecturerUsername.ToUpperInvariant();
        var lecturerId = await db.Users.AsNoTracking()
            .Where(u => u.NormalizedUserName == lecturerNormalized)
            .Select(u => (Guid?)u.Id)
            .FirstOrDefaultAsync(cancellationToken);

        var hotModuleId = await db.Modules.AsNoTracking()
            .Where(m => m.Code == DatabaseSeeder.HotModuleCode)
            .Select(m => (Guid?)m.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (adminId is null || lecturerId is null || hotModuleId is null)
        {
            return new StepOutcome(0, "skipped: admin, L00001 or CS3099 missing");
        }

        var now = clock.GetUtcNow();

        var publishAt = await db.ResultsPublications.AsNoTracking()
            .Where(p => p.AcademicYear == SeededResultsAcademicYear && p.Semester == Semester.Autumn)
            .OrderByDescending(p => p.CreatedAt)
            .Select(p => (DateTimeOffset?)p.PublishAt)
            .FirstOrDefaultAsync(cancellationToken);
        var resultsLive = publishAt is { } instant && instant <= now;

        var springClosesAt = await db.EnrolmentWindows.AsNoTracking()
            .Where(w => w.AcademicYear == CurrentAcademicYear && w.Semester == Semester.Spring)
            .Select(w => (DateTimeOffset?)w.ClosesAt)
            .FirstOrDefaultAsync(cancellationToken);

        db.Announcements.AddRange(
            new Announcement
            {
                Id = Guid.CreateVersion7(),
                Scope = AnnouncementScope.University,
                Title = resultsLive ? "Autumn 2025/26 results are published" : "Autumn 2025/26 results are coming",
                Body = resultsLive
                    ? "Sign in to your dashboard to see your marks and your average so far."
                    : "Your dashboard shows a countdown to the moment they are published.",
                Pinned = true,
                PublishedAt = now,
                ExpiresAt = publishAt?.AddDays(7),
                CreatedByUserId = adminId.Value,
                CreatedAt = now,
                UpdatedAt = now,
            },
            new Announcement
            {
                Id = Guid.CreateVersion7(),
                Scope = AnnouncementScope.University,
                Title = "Spring 2026/27 enrolment is open",
                Body = "Enrol on spring modules from the catalogue while places last. Each module shows when enrolment closes.",
                Pinned = false,
                PublishedAt = now,
                ExpiresAt = springClosesAt,
                CreatedByUserId = adminId.Value,
                CreatedAt = now,
                UpdatedAt = now,
            },
            new Announcement
            {
                Id = Guid.CreateVersion7(),
                Scope = AnnouncementScope.Module,
                ModuleId = hotModuleId,
                Title = "Welcome to Advanced Machine Learning",
                Body = "Welcome to CS3099. The module has 30 places, so enrol early if you have not already.",
                Pinned = false,
                PublishedAt = now,
                ExpiresAt = null,
                CreatedByUserId = lecturerId.Value,
                CreatedAt = now,
                UpdatedAt = now,
            });
        await db.SaveChangesAsync(cancellationToken);

        return new StepOutcome(3, null);
    }

    // 11. demo_reset_hot_module (always, demo): CS3099's 30 places come back on every start.
    private static async Task<StepOutcome> ResetDemoHotModuleAsync(RushDayDbContext db, TimeProvider clock, CancellationToken cancellationToken)
    {
        var withdrawn = await WithdrawDemoHotModuleEnrolmentsAsync(db, cancellationToken);
        if (withdrawn == 0)
        {
            return new StepOutcome(0, null);
        }

        var hotModuleId = await db.Modules.AsNoTracking()
            .Where(m => m.Code == DatabaseSeeder.HotModuleCode)
            .Select(m => (Guid?)m.Id)
            .FirstOrDefaultAsync(cancellationToken);

        Audit(db, clock, AuditActions.SystemDemoReset, DatabaseSeeder.HotModuleCode, new { moduleCode = DatabaseSeeder.HotModuleCode, withdrawn }, hotModuleId);
        await db.SaveChangesAsync(cancellationToken);

        return new StepOutcome(withdrawn, $"{withdrawn} self-enrolments withdrawn");
    }

    // 12. demo_autumn_cohort (once, demo): the demo lecturer's 100 year-1 students on CS3001, nothing marked yet.
    private static async Task<StepOutcome> InsertDemoAutumnCohortAsync(RushDayDbContext db, CancellationToken cancellationToken)
    {
        var currentYear = await db.AcademicSettings.AsNoTracking()
            .Where(s => s.Id == AcademicSettings.SingletonId)
            .Select(s => s.AcademicYear)
            .SingleOrDefaultAsync(cancellationToken);
        if (currentYear is null)
        {
            return new StepOutcome(0, "skipped: academic_settings missing");
        }

        var inserted = await db.Database.ExecuteSqlRawAsync(
            DemoCohortSql,
            [
                new NpgsqlParameter("currentYear", currentYear),
                new NpgsqlParameter("cohortSize", DemoCohortSize),
                new NpgsqlParameter("moduleCode", DemoCohortModuleCode),
            ],
            cancellationToken);

        return new StepOutcome(inserted, $"{inserted} enrolments on {DemoCohortModuleCode} for {currentYear}");
    }

    // 13. reconcile_enrolled_count (always, last): sees every demo step's changes.
    private static async Task<StepOutcome> ReconcileEnrolledCountAsync(RushDayDbContext db, ILogger logger, CancellationToken cancellationToken)
    {
        var corrected = await ReconcileEnrolledCountAsync(db, cancellationToken);

        var overCapacity = await db.Modules.AsNoTracking()
            .Where(m => m.EnrolledCount > m.Capacity)
            .OrderBy(m => m.Code)
            .Select(m => new { m.Code, m.Capacity, m.EnrolledCount })
            .ToListAsync(cancellationToken);

        foreach (var module in overCapacity)
        {
            logger.LogWarning(
                "Module {ModuleCode} is over capacity: {EnrolledCount} active enrolments for {Capacity} places. Raise the capacity, trim to capacity or admin-withdraw students.",
                module.Code,
                module.EnrolledCount,
                module.Capacity);
        }

        return new StepOutcome(corrected, overCapacity.Count == 0 ? null : $"{overCapacity.Count} module(s) over capacity");
    }

    private static string NewSecurityStamp() => Guid.NewGuid().ToString("N").ToUpperInvariant();

    private readonly record struct StepOutcome(int RowsAffected, string? Notes);
}
