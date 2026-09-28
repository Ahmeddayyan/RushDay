using System.Diagnostics;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;
using RushDay.Domain.Announcements;
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
/// The idempotent startup steps of 01-domain-and-data.md section 6. They run after the migration and the seeder,
/// against a database that may already hold 20,000 students. Each step commits its own transaction together with
/// its <c>data_backfills</c> row, so a crash between steps resumes at the failed step on the next start.
/// </summary>
public static class StartupBackfills
{
    public static class StepNames
    {
        public const string ReconcileEnrolledCount = "reconcile_enrolled_count";
        public const string Roles = "roles";
        public const string AcademicSettings = "academic_settings";
        public const string EnrolmentWindows = "enrolment_windows_2026_27";
        public const string ResultsPublication = "results_publication_autumn_2025_26";
        public const string LecturersAndAssignments = "lecturers_and_assignments";
        public const string AdminAccount = "admin_account";
        public const string DemoAccounts = "demo_accounts";
        public const string DemoAnnouncements = "demo_announcements";
    }

    public const string CurrentAcademicYear = "2026/27";
    public const string SeededResultsAcademicYear = "2025/26";
    public const string SeededResultsNote = "Seeded autumn results";
    public const string AdminSkippedNote = "skipped: no password";

    /// <summary>Set-based reconciliation of modules.enrolled_count; also run by the migration and the ops reconcile action.</summary>
    public const string ReconcileEnrolledCountSql =
        "UPDATE modules m SET enrolled_count = c.n FROM (SELECT module_id, count(*) n FROM enrolments WHERE status = 'Active' GROUP BY module_id) c WHERE m.id = c.module_id AND m.enrolled_count <> c.n;";

    public const string ReconcileZeroEnrolledCountSql =
        "UPDATE modules m SET enrolled_count = 0 WHERE enrolled_count <> 0 AND NOT EXISTS (SELECT 1 FROM enrolments e WHERE e.module_id = m.id AND e.status = 'Active');";

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

    private static readonly PasswordHasher<ApplicationUser> Hasher = new();

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

        var results = new List<BackfillResult>(9)
        {
            await RunStepAsync(db, StepNames.ReconcileEnrolledCount, once: false, ct => ReconcileEnrolledCountAsync(db, logger, ct), clock, logger, cancellationToken),
            await RunStepAsync(db, StepNames.Roles, once: false, ct => InsertRolesAsync(db, ct), clock, logger, cancellationToken),
            await RunStepAsync(db, StepNames.AcademicSettings, once: true, ct => InsertAcademicSettingsAsync(db, options, clock, ct), clock, logger, cancellationToken),
            await RunStepAsync(db, StepNames.EnrolmentWindows, once: true, ct => InsertEnrolmentWindowsAsync(db, clock, ct), clock, logger, cancellationToken),
            await RunStepAsync(db, StepNames.ResultsPublication, once: true, ct => InsertSeedPublicationAsync(db, options, clock, ct), clock, logger, cancellationToken),
            await RunStepAsync(db, StepNames.LecturersAndAssignments, once: true, ct => InsertLecturersAsync(db, clock, ct), clock, logger, cancellationToken),
            await RunStepAsync(db, StepNames.AdminAccount, once: false, ct => EnsureAdminAccountAsync(db, options, clock, logger, ct), clock, logger, cancellationToken),
        };

        if (options.DemoEnabled)
        {
            results.Add(await RunStepAsync(db, StepNames.DemoAccounts, once: false, ct => InsertDemoAccountsAsync(db, ct), clock, logger, cancellationToken));
            results.Add(await RunStepAsync(db, StepNames.DemoAnnouncements, once: true, ct => InsertDemoAnnouncementsAsync(db, clock, ct), clock, logger, cancellationToken));
        }
        else
        {
            logger.LogInformation("Backfills {DemoAccounts} and {DemoAnnouncements} not run: demo mode is off.", StepNames.DemoAccounts, StepNames.DemoAnnouncements);
        }

        return results;
    }

    /// <summary>Runs the two reconciliation statements and returns the number of module rows corrected.</summary>
    public static async Task<int> ReconcileEnrolledCountAsync(RushDayDbContext db, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(db);

        var corrected = await db.Database.ExecuteSqlRawAsync(ReconcileEnrolledCountSql, cancellationToken);
        corrected += await db.Database.ExecuteSqlRawAsync(ReconcileZeroEnrolledCountSql, cancellationToken);
        return corrected;
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

    // 1. reconcile_enrolled_count (always)
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
                "Module {ModuleCode} is over capacity: {EnrolledCount} active enrolments for {Capacity} places. Raise the capacity or admin-withdraw students.",
                module.Code,
                module.EnrolledCount,
                module.Capacity);
        }

        return new StepOutcome(corrected, overCapacity.Count == 0 ? null : $"{overCapacity.Count} module(s) over capacity");
    }

    // 2. roles (always)
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

    // 3. academic_settings (once)
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
            UpdatedAt = clock.GetUtcNow(),
        });
        await db.SaveChangesAsync(cancellationToken);

        return new StepOutcome(1, null);
    }

    // 4. enrolment_windows_2026_27 (once)
    private static async Task<StepOutcome> InsertEnrolmentWindowsAsync(RushDayDbContext db, TimeProvider clock, CancellationToken cancellationToken)
    {
        if (await db.EnrolmentWindows.AnyAsync(w => w.AcademicYear == CurrentAcademicYear, cancellationToken))
        {
            return new StepOutcome(0, "exists");
        }

        var now = clock.GetUtcNow();
        db.EnrolmentWindows.AddRange(
            new EnrolmentWindow
            {
                Id = Guid.CreateVersion7(),
                AcademicYear = CurrentAcademicYear,
                Semester = Semester.Autumn,
                OpensAt = new DateTimeOffset(2026, 9, 14, 9, 0, 0, TimeSpan.Zero),
                ClosesAt = new DateTimeOffset(2026, 10, 2, 17, 0, 0, TimeSpan.Zero),
                WithdrawalDeadlineAt = new DateTimeOffset(2026, 10, 30, 17, 0, 0, TimeSpan.Zero),
                CreatedByUserId = null,
                UpdatedAt = now,
            },
            new EnrolmentWindow
            {
                Id = Guid.CreateVersion7(),
                AcademicYear = CurrentAcademicYear,
                Semester = Semester.Spring,
                OpensAt = new DateTimeOffset(2026, 9, 14, 9, 0, 0, TimeSpan.Zero),
                ClosesAt = new DateTimeOffset(2027, 1, 29, 17, 0, 0, TimeSpan.Zero),
                WithdrawalDeadlineAt = new DateTimeOffset(2027, 2, 26, 17, 0, 0, TimeSpan.Zero),
                CreatedByUserId = null,
                UpdatedAt = now,
            });
        await db.SaveChangesAsync(cancellationToken);

        return new StepOutcome(2, null);
    }

    // 5. results_publication_autumn_2025_26 (once)
    private static async Task<StepOutcome> InsertSeedPublicationAsync(RushDayDbContext db, StartupBackfillOptions options, TimeProvider clock, CancellationToken cancellationToken)
    {
        if (await db.ResultsPublications.AnyAsync(p => p.AcademicYear == SeededResultsAcademicYear && p.Semester == Semester.Autumn, cancellationToken))
        {
            return new StepOutcome(0, "exists");
        }

        var pending = db.Grades.Where(g => g.PublicationId == null && g.Status == GradeStatus.Published);
        var gradeCount = await pending.CountAsync(cancellationToken);
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

    // 6. lecturers_and_assignments (once)
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

    // 7. admin_account (always)
    private static async Task<StepOutcome> EnsureAdminAccountAsync(RushDayDbContext db, StartupBackfillOptions options, TimeProvider clock, ILogger logger, CancellationToken cancellationToken)
    {
        if (await db.UserRoles.AnyAsync(ur => ur.RoleId == RushDayRoles.AdminId, cancellationToken))
        {
            return new StepOutcome(0, "exists");
        }

        string password;
        bool mustChangePassword;
        bool isDemo;
        if (!string.IsNullOrEmpty(options.BootstrapAdminPassword))
        {
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
            UserName = DemoAccounts.AdminUsername,
            NormalizedUserName = DemoAccounts.AdminUsername.ToUpperInvariant(),
            DisplayName = options.DemoEnabled ? DemoAccounts.AdminDisplayName : DemoAccounts.NonDemoAdminDisplayName,
            MustChangePassword = mustChangePassword,
            IsDemo = isDemo,
            LockoutEnabled = true,
            SecurityStamp = NewSecurityStamp(),
            ConcurrencyStamp = Guid.NewGuid().ToString(),
            CreatedAt = clock.GetUtcNow(),
        };
        admin.PasswordHash = Hasher.HashPassword(admin, password);

        db.Users.Add(admin);
        db.UserRoles.Add(new IdentityUserRole<Guid> { UserId = admin.Id, RoleId = RushDayRoles.AdminId });
        await db.SaveChangesAsync(cancellationToken);

        return new StepOutcome(1, mustChangePassword ? "created with bootstrap password; must change" : "created with demo password");
    }

    // 8. demo_accounts (always, demo only)
    private static async Task<StepOutcome> InsertDemoAccountsAsync(RushDayDbContext db, CancellationToken cancellationToken)
    {
        // One hash per role: hashing 20,000 passwords on 0.1 CPU would take hours (00-overview.md D7).
        var studentHash = Hasher.HashPassword(new ApplicationUser(), DemoAccounts.StudentPassword);
        var lecturerHash = Hasher.HashPassword(new ApplicationUser(), DemoAccounts.LecturerPassword);

        var rows = await db.Database.ExecuteSqlRawAsync(DemoStudentUsersSql, [new NpgsqlParameter("studentHash", studentHash)], cancellationToken);
        rows += await db.Database.ExecuteSqlRawAsync(DemoStudentRolesSql, [new NpgsqlParameter("studentRoleId", RushDayRoles.StudentId)], cancellationToken);
        rows += await db.Database.ExecuteSqlRawAsync(DemoLecturerUsersSql, [new NpgsqlParameter("lecturerHash", lecturerHash)], cancellationToken);
        rows += await db.Database.ExecuteSqlRawAsync(DemoLecturerRolesSql, [new NpgsqlParameter("lecturerRoleId", RushDayRoles.LecturerId)], cancellationToken);

        return new StepOutcome(rows, null);
    }

    // 9. demo_announcements (once, demo only)
    private static async Task<StepOutcome> InsertDemoAnnouncementsAsync(RushDayDbContext db, TimeProvider clock, CancellationToken cancellationToken)
    {
        var adminNormalized = DemoAccounts.AdminUsername.ToUpperInvariant();
        var adminId = await db.Users.AsNoTracking()
            .Where(u => u.NormalizedUserName == adminNormalized)
            .Select(u => (Guid?)u.Id)
            .FirstOrDefaultAsync(cancellationToken)
            ?? await db.UserRoles.AsNoTracking()
                .Where(ur => ur.RoleId == RushDayRoles.AdminId)
                .Select(ur => (Guid?)ur.UserId)
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
        db.Announcements.AddRange(
            new Announcement
            {
                Id = Guid.CreateVersion7(),
                Scope = AnnouncementScope.University,
                Title = "Autumn 2025/26 results publish on 28 September at 10:00",
                Body = "Autumn 2025/26 results will be published on 28 September 2026 at 10:00 (Europe/London). "
                     + "Sign in to your dashboard to see your marks the moment they go live.",
                Pinned = true,
                PublishedAt = now,
                CreatedByUserId = adminId.Value,
                CreatedAt = now,
                UpdatedAt = now,
            },
            new Announcement
            {
                Id = Guid.CreateVersion7(),
                Scope = AnnouncementScope.University,
                Title = "Spring 2026/27 enrolment is open until 29 January",
                Body = "Enrol on spring modules from the catalogue while places last. The window closes on 29 January 2027 at 17:00 (Europe/London); "
                     + "you can withdraw from a spring module until 26 February 2027.",
                Pinned = false,
                PublishedAt = now,
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
                Body = "Welcome to CS3099. Lectures and labs are on your timetable from the first week of the spring semester. "
                     + "The module has 30 places, so enrol early if you have not already.",
                Pinned = false,
                PublishedAt = now,
                CreatedByUserId = lecturerId.Value,
                CreatedAt = now,
                UpdatedAt = now,
            });
        await db.SaveChangesAsync(cancellationToken);

        return new StepOutcome(3, null);
    }

    private static string NewSecurityStamp() => Guid.NewGuid().ToString("N").ToUpperInvariant();

    private readonly record struct StepOutcome(int RowsAffected, string? Notes);
}
