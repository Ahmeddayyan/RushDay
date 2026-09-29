using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using RushDay.Domain.Enrolments;
using RushDay.Domain.Grades;
using RushDay.Domain.Modules;
using RushDay.Domain.Settings;
using RushDay.Domain.Users;
using RushDay.Infrastructure.Persistence;
using RushDay.Infrastructure.Seeding;

namespace RushDay.IntegrationTests.Persistence;

/// <summary>
/// Automates docs/spec/01-domain-and-data.md section 9 steps 1-4 against the collection's already-migrated,
/// already-seeded database: the shared <see cref="RushDayApiFactory"/> does the migrate -> seed 300 students ->
/// backfill-once rehearsal itself at startup (its <c>Settings</c>/<c>StartupTasks</c>), so a derived host on the same
/// database is the "already applied InitialCreate and PortalAndIdentity" state section 9 describes. From there:
/// running the backfills again is idempotent, the running model has no pending migration, demo mode can be switched
/// off and back on cleanly, a bootstrap password the policy rejects never creates an account, and year-scoped
/// reconciliation zeroes modules that only carry earlier-year enrolments.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class MigrationOnSeededDatabaseTests(RushDayApiFactory factory)
{
    [Fact]
    public async Task Running_the_backfills_twice_with_demo_on_is_idempotent()
    {
        await using var host = factory.Derive();
        await using var scope = host.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<RushDayDbContext>();
        var options = PersistenceTestSupport.BackfillOptions(demoEnabled: true);

        var first = await StartupBackfills.RunAsync(db, options, factory.Clock, NullLogger.Instance);
        var usersAfterFirst = await db.Users.CountAsync();
        Assert.NotEmpty(first);

        var second = await StartupBackfills.RunAsync(db, options, factory.Clock, NullLogger.Instance);
        var usersAfterSecond = await db.Users.CountAsync();

        Assert.Equal(usersAfterFirst, usersAfterSecond);
        PersistenceTestSupport.AssertAllConverged(second);

        // Section 9 step 4: the running model matches the latest migration exactly (an in-process equivalent of
        // `dotnet ef migrations has-pending-model-changes`, so this needs no database round trip at all).
        Assert.False(db.Database.HasPendingModelChanges());

        // Section 9 step 3: every published grade is attached to a publication.
        Assert.False(await db.Grades.AnyAsync(g => g.PublicationId == null && g.Status == GradeStatus.Published));

        // CS3099: capacity 30, enrolled_count consistent with a fresh count of active current-year enrolments -
        // whatever self-enrolments other tests left on the hot module, the "always" reset-and-reconcile steps
        // converge it back to the true count rather than to the exact numbers a 20,000-student rehearsal would show.
        var (cs3099Id, cs3099Capacity, cs3099Enrolled) = await ModuleCountsAsync(db, "CS3099");
        Assert.Equal(30, cs3099Capacity);
        Assert.Equal(await ActiveCurrentYearCountAsync(db, cs3099Id), cs3099Enrolled);

        // CS3001: the demo lecturer's autumn cohort (step 12) plus whatever else this suite enrolled there.
        var (cs3001Id, cs3001Capacity, cs3001Enrolled) = await ModuleCountsAsync(db, "CS3001");
        Assert.Equal(1500, cs3001Capacity);
        Assert.True(cs3001Enrolled >= StartupBackfills.DemoCohortSize, $"Expected at least the {StartupBackfills.DemoCohortSize}-student demo cohort on CS3001, found {cs3001Enrolled}.");
        Assert.Equal(await ActiveCurrentYearCountAsync(db, cs3001Id), cs3001Enrolled);

        // module_lecturers: L00001 leads both CS3099 and CS3001, and L00006 teaches CS3099 (section 6 step 7,
        // section 9 step 3) - independent of student count, since the module and lecturer lists never depend on it.
        Assert.Equal("L00001", await LeaderStaffNumberAsync(db, "CS3099"));
        Assert.Equal("L00001", await LeaderStaffNumberAsync(db, "CS3001"));
        Assert.Equal("L00006", await TeacherStaffNumberAsync(db, "CS3099"));

        // audit_events is append-only (full coverage lives in Staff/AuditTests; a spot check here since section 9
        // step 3 asserts exactly this against the same rehearsal database).
        var anyAuditId = await db.AuditEvents.Select(a => a.Id).FirstAsync();
        var updateAttempt = await Assert.ThrowsAsync<PostgresException>(
            () => db.Database.ExecuteSqlAsync($"UPDATE audit_events SET action = 'tampered-by-test' WHERE id = {anyAuditId}"));
        Assert.Contains("append-only", updateAttempt.MessageText, StringComparison.Ordinal);
    }

    /// <summary>
    /// Runs the backfills once more with <c>DemoEnabled = false</c> and asserts every <c>is_demo</c> row is disabled
    /// with a changed security stamp, then once more with demo on and asserts they are enabled again (section 9
    /// step 6). Restores demo accounts to enabled in a <c>finally</c> so a failed assertion here cannot strand the
    /// rest of the suite with every demo account locked out.
    /// </summary>
    [Fact]
    public async Task Demo_off_disables_every_demo_account()
    {
        await using var host = factory.Derive();
        await using var scope = host.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<RushDayDbContext>();
        var onOptions = PersistenceTestSupport.BackfillOptions(demoEnabled: true);

        var before = await DemoStampsAsync(db);
        Assert.NotEmpty(before);
        Assert.All(before.Values, s => Assert.Null(s.DisabledAt));

        try
        {
            var offOptions = PersistenceTestSupport.BackfillOptions(demoEnabled: false);
            var offResults = await StartupBackfills.RunAsync(db, offOptions, factory.Clock, NullLogger.Instance);

            var disableStep = offResults.Single(r => r.Name == StartupBackfills.StepNames.DemoAccountsDisable);
            Assert.True(disableStep.RowsAffected > 0);

            var afterOff = await DemoStampsAsync(db);
            Assert.Equal(before.Count, afterOff.Count);
            foreach (var (id, was) in before)
            {
                var now = afterOff[id];
                Assert.NotNull(now.DisabledAt);
                Assert.NotEqual(was.SecurityStamp, now.SecurityStamp);
            }

            // Demo on again: HealDemoUsersSql clears disabled_at and, because disabled_at was set, rotates the stamp once more.
            await StartupBackfills.RunAsync(db, onOptions, factory.Clock, NullLogger.Instance);

            var afterOn = await DemoStampsAsync(db);
            Assert.Equal(before.Count, afterOn.Count);
            Assert.All(afterOn.Values, s => Assert.Null(s.DisabledAt));
            foreach (var (id, off) in afterOff)
            {
                Assert.NotEqual(off.SecurityStamp, afterOn[id].SecurityStamp);
            }

            // A further run with demo on is fully idempotent: nothing left to heal.
            var steady = await StartupBackfills.RunAsync(db, onOptions, factory.Clock, NullLogger.Instance);
            PersistenceTestSupport.AssertAllConverged(steady);
        }
        finally
        {
            // Leave demo accounts enabled for the rest of the suite even if an assertion above threw.
            await StartupBackfills.RunAsync(db, onOptions, factory.Clock, NullLogger.Instance);
        }
    }

    /// <summary>
    /// Runs demo off with a bootstrap password the policy blocks (common-password list) and asserts no administrator
    /// was created; the step's own notes name the rejection (<see cref="StartupBackfills.AdminPasswordRejectedNote"/>).
    /// Every enabled non-demo administrator this suite has already provisioned is disabled for the duration (else the
    /// step exits early on "a usable administrator already exists" before it ever looks at the password) and restored
    /// in a <c>finally</c>.
    /// </summary>
    [Fact]
    public async Task Bootstrap_password_rejected_by_policy_is_not_used()
    {
        await using var host = factory.Derive();
        await using var scope = host.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<RushDayDbContext>();

        var adminRoleId = RushDayRoles.AdminId;
        var toRestore = await db.Users.AsNoTracking()
            .Where(u => u.DisabledAt == null && !u.IsDemo && db.UserRoles.Any(ur => ur.UserId == u.Id && ur.RoleId == adminRoleId))
            .Select(u => u.Id)
            .ToListAsync();

        var username = TestAccounts.NewUsername("boot");
        var options = PersistenceTestSupport.BackfillOptions(demoEnabled: false, bootstrapAdminUsername: username, bootstrapAdminPassword: "password1234");

        try
        {
            if (toRestore.Count > 0)
            {
                await db.Users.Where(u => toRestore.Contains(u.Id)).ExecuteUpdateAsync(s => s.SetProperty(u => u.DisabledAt, factory.Clock.GetUtcNow()));
            }

            var results = await StartupBackfills.RunAsync(db, options, factory.Clock, NullLogger.Instance);

            var adminStep = results.Single(r => r.Name == StartupBackfills.StepNames.AdminAccount);
            Assert.Equal(0, adminStep.RowsAffected);
            Assert.Equal(StartupBackfills.AdminPasswordRejectedNote, adminStep.Notes);

            var normalized = username.ToUpperInvariant();
            Assert.False(await db.Users.AsNoTracking().AnyAsync(u => u.NormalizedUserName == normalized));

            var row = await db.DataBackfills.AsNoTracking().SingleAsync(b => b.Name == StartupBackfills.StepNames.AdminAccount);
            Assert.Equal(StartupBackfills.AdminPasswordRejectedNote, row.Notes);
        }
        finally
        {
            if (toRestore.Count > 0)
            {
                await db.Users.Where(u => toRestore.Contains(u.Id)).ExecuteUpdateAsync(s => s.SetProperty(u => u.DisabledAt, (DateTimeOffset?)null));
            }
        }
    }

    /// <summary>
    /// Changes <c>academic_settings.academic_year</c> and asserts the reconciliation zeroes a module that only has
    /// earlier-year enrolments (D28). Wrapped in one uncommitted transaction (the module, its enrolment and the
    /// settings-row change all roll back on disposal), because <see cref="StartupBackfills.ReconcileEnrolledCountAsync"/>
    /// unlike <see cref="StartupBackfills.RunAsync"/> starts no transaction of its own, so nesting is safe here.
    /// </summary>
    [Fact]
    public async Task Enrolled_count_is_scoped_to_the_current_year()
    {
        await using var host = factory.Derive();
        await using var scope = host.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<RushDayDbContext>();
        await using var transaction = await db.Database.BeginTransactionAsync();

        const string code = "ZZ9101";
        var moduleId = Guid.CreateVersion7();
        var studentId = await db.Students.Select(s => s.Id).FirstAsync();
        var currentYear = await db.AcademicSettings.AsNoTracking().Where(s => s.Id == AcademicSettings.SingletonId).Select(s => s.AcademicYear).SingleAsync();

        db.Modules.Add(new Module
        {
            Id = moduleId,
            Code = code,
            Department = "ZZ",
            Title = "Persistence rehearsal module",
            Credits = 15,
            Capacity = 50,
            Semester = Semester.Autumn,
            EnrolledCount = 1,
        });
        db.Enrolments.Add(new Enrolment
        {
            Id = Guid.CreateVersion7(),
            StudentId = studentId,
            ModuleId = moduleId,
            EnrolledAt = new DateTimeOffset(2025, 9, 20, 9, 0, 0, TimeSpan.Zero),
            Status = EnrolmentStatus.Active,
            Source = EnrolmentSource.Admin,
            AcademicYear = currentYear,
        });
        await db.SaveChangesAsync();

        // Still consistent while the module's one enrolment matches the current academic year.
        await StartupBackfills.ReconcileEnrolledCountAsync(db);
        Assert.Equal(1, await ModuleEnrolledCountAsync(db, moduleId));

        // The academic year rolls forward: the module's only enrolment is now an earlier year's.
        const string nextYear = "2099/00";
        await db.Database.ExecuteSqlAsync($"UPDATE academic_settings SET academic_year = {nextYear} WHERE id = {AcademicSettings.SingletonId}");

        var corrected = await StartupBackfills.ReconcileEnrolledCountAsync(db);

        Assert.True(corrected > 0);
        Assert.Equal(0, await ModuleEnrolledCountAsync(db, moduleId));

        // No commit: disposing the transaction rolls back the settings change, the module and the enrolment.
    }

    private static async Task<(Guid Id, int Capacity, int EnrolledCount)> ModuleCountsAsync(RushDayDbContext db, string code)
    {
        var module = await db.Modules.AsNoTracking().Where(m => m.Code == code).Select(m => new { m.Id, m.Capacity, m.EnrolledCount }).SingleAsync();
        return (module.Id, module.Capacity, module.EnrolledCount);
    }

    private static Task<int> ModuleEnrolledCountAsync(RushDayDbContext db, Guid moduleId) =>
        db.Modules.AsNoTracking().Where(m => m.Id == moduleId).Select(m => m.EnrolledCount).SingleAsync();

    private static Task<int> ActiveCurrentYearCountAsync(RushDayDbContext db, Guid moduleId) =>
        db.Enrolments.AsNoTracking().CountAsync(e => e.ModuleId == moduleId && e.Status == EnrolmentStatus.Active && e.AcademicYear == StartupBackfills.CurrentAcademicYear);

    private static Task<string?> LeaderStaffNumberAsync(RushDayDbContext db, string code) =>
        StaffNumberAsync(db, code, ModuleLecturerRole.Leader);

    private static Task<string?> TeacherStaffNumberAsync(RushDayDbContext db, string code) =>
        StaffNumberAsync(db, code, ModuleLecturerRole.Teacher);

    private static Task<string?> StaffNumberAsync(RushDayDbContext db, string code, ModuleLecturerRole role) =>
        (from ml in db.ModuleLecturers.AsNoTracking()
         join m in db.Modules.AsNoTracking() on ml.ModuleId equals m.Id
         join l in db.Lecturers.AsNoTracking() on ml.LecturerId equals l.Id
         where m.Code == code && ml.Role == role
         select l.StaffNumber).SingleOrDefaultAsync();

    private static async Task<Dictionary<Guid, (DateTimeOffset? DisabledAt, string SecurityStamp)>> DemoStampsAsync(RushDayDbContext db)
    {
        var rows = await db.Users.AsNoTracking().Where(u => u.IsDemo).Select(u => new { u.Id, u.DisabledAt, u.SecurityStamp }).ToListAsync();
        return rows.ToDictionary(r => r.Id, r => (r.DisabledAt, r.SecurityStamp ?? string.Empty));
    }

    /// <summary>
    /// Root cause of the order-dependent CI failure (GitHub Actions run 36567479154, commit 81df00d):
    /// <c>demo_accounts</c>'s admin hash-reuse picked its reference from "any <c>is_demo</c> administrator, earliest
    /// <c>created_at</c>" rather than the one named bootstrap account (<see cref="DemoAccounts.AdminUsername"/>), unlike
    /// the student and lecturer lookups, which are keyed by username (<see cref="StoredHashAsync"/>). A demo
    /// administrator can provision another admin account through the ordinary account routes
    /// (<see cref="RushDay.IntegrationTests.Auth.DemoActorTests.Accounts_a_demo_administrator_provisions_are_demo_accounts"/>):
    /// the new account is also <c>is_demo</c> (so it is read-only and dies with the demo) but keeps its own,
    /// independently generated password. With two such rows and no tiebreaker, Postgres does not guarantee which one
    /// <c>OrderBy(created_at).First()</c> returns on any given run - it picked the bootstrap admin on one of the two
    /// back-to-back calls the idempotency test makes and the provisioned account on the other - so a run could decide
    /// the "reference" hash does not verify and stamp every <c>is_demo</c> admin row, including the provisioned one,
    /// with a freshly hashed copy of the shared demo admin password: not idempotent, and a real password silently
    /// overwritten on a start that should have changed nothing. This test reaches the same state a live public demo
    /// can reach (a demo actor provisioning an account), then forces the same bad pick deterministically - by giving
    /// the provisioned account an earlier <c>created_at</c> - instead of depending on suite ordering or on how
    /// Postgres happens to break the tie.
    /// </summary>
    [Fact]
    public async Task An_admin_a_demo_actor_provisions_keeps_its_own_password_across_backfills()
    {
        await using var host = factory.Derive();
        await using var scope = host.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<RushDayDbContext>();
        var options = PersistenceTestSupport.BackfillOptions(demoEnabled: true);

        // Known state first (this suite runs the demo admin through demo-off elsewhere, e.g.
        // Bootstrap_password_rejected_by_policy_is_not_used's own demoEnabled: false call disables every is_demo
        // account as a side effect of exercising step 9): heal it here rather than depend on suite order.
        await StartupBackfills.RunAsync(db, options, factory.Clock, NullLogger.Instance);

        await using var probe = await ProbeApp.StartAsync(factory);
        using var demoAdmin = await probe.LoginAsync(DemoAccounts.AdminUsername, DemoAccounts.AdminPassword);
        using var response = await demoAdmin.PostAsync("/api/probe/accounts", null);
        var provisionedId = (await response.ReadJsonAsync()).GetProperty("id").GetGuid();
        Assert.True((await factory.ReadUserAsync(provisionedId)).IsDemo);

        // Force the provisioned account to sort first, deterministically, instead of hoping the suite reproduces
        // whichever tie-break Postgres would otherwise apply.
        await db.Database.ExecuteSqlAsync($"UPDATE users SET created_at = created_at - interval '1 hour' WHERE id = {provisionedId}");
        var hashBefore = (await factory.ReadUserAsync(provisionedId)).PasswordHash;

        await StartupBackfills.RunAsync(db, options, factory.Clock, NullLogger.Instance);
        var second = await StartupBackfills.RunAsync(db, options, factory.Clock, NullLogger.Instance);

        PersistenceTestSupport.AssertAllConverged(second);
        Assert.Equal(hashBefore, (await factory.ReadUserAsync(provisionedId)).PasswordHash);
    }
}

/// <summary>Shared by <see cref="MigrationOnSeededDatabaseTests"/> and <see cref="BackfillRecoveryTests"/>.</summary>
internal static class PersistenceTestSupport
{
    /// <summary>Built the way <c>StartupTasks.RunAsync</c> builds it, from the Demo, Bootstrap, Branding and Database settings.</summary>
    public static StartupBackfillOptions BackfillOptions(bool demoEnabled, string? bootstrapAdminUsername = null, string? bootstrapAdminPassword = null) => new()
    {
        DemoEnabled = demoEnabled,
        BootstrapAdminUsername = string.IsNullOrWhiteSpace(bootstrapAdminUsername) ? "admin" : bootstrapAdminUsername,
        BootstrapAdminPassword = bootstrapAdminPassword,
        InstitutionName = StartupBackfillOptions.DefaultInstitutionName,
        InstitutionShortName = StartupBackfillOptions.DefaultInstitutionShortName,
        TimeZone = StartupBackfillOptions.DefaultTimeZone,
        SeedResultsDay = RushDayApiFactory.SeedResultsDay,
    };

    /// <summary>
    /// A converged run must affect 0 rows everywhere (01-domain-and-data.md section 6); names the offending step
    /// first so a failure here (like GitHub Actions run 36567479154's "demo_accounts, RowsAffected = 2") says which
    /// step broke idempotency without a trip through the notes column.
    /// </summary>
    public static void AssertAllConverged(IReadOnlyList<BackfillResult> results) =>
        Assert.All(results, r => Assert.True(r.RowsAffected == 0, $"{r.Name}: expected 0 rows affected on a converged run, got {r.RowsAffected} ({r.Notes ?? "no notes"})."));
}
