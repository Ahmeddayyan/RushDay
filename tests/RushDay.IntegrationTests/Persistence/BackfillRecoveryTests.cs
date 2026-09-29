using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using RushDay.Infrastructure.Persistence;
using RushDay.Infrastructure.Seeding;

namespace RushDay.IntegrationTests.Persistence;

/// <summary>
/// docs/spec/06-implementation-plan.md's S11 "Owns" list ("a failing step simulated after backfill step 6 and a
/// restart that completes") and 01-domain-and-data.md section 9 step 6's preamble: <see cref="StartupBackfills"/> is
/// resumable because every step commits its own transaction (and its own <c>data_backfills</c> row) before the next
/// one starts, so a crash between steps leaves the completed ones committed and resumes at the failed step on the
/// next start. A crash is simulated with a throwing <see cref="ILogger"/> that raises right after
/// <c>StepNames.EnrolmentWindows</c> (the sixth step with demo on: roles, academic_settings,
/// results_publication_autumn_2025_26, relabel_v0_self_enrolments, admin_account, enrolment_windows_2026_27, ...)
/// logs its completion - that is, strictly after its own transaction has already committed.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class BackfillRecoveryTests(RushDayApiFactory factory)
{
    /// <summary>The demo-on step order (<see cref="StartupBackfills.RunAsync"/>), used both to place the crash and to assert full recovery.</summary>
    private static readonly string[] DemoOnStepOrder =
    [
        StartupBackfills.StepNames.Roles,
        StartupBackfills.StepNames.AcademicSettings,
        StartupBackfills.StepNames.ResultsPublication,
        StartupBackfills.StepNames.RelabelV0SelfEnrolments,
        StartupBackfills.StepNames.AdminAccount,
        StartupBackfills.StepNames.EnrolmentWindows,
        StartupBackfills.StepNames.LecturersAndAssignments,
        StartupBackfills.StepNames.DemoAccounts,
        StartupBackfills.StepNames.DemoAnnouncements,
        StartupBackfills.StepNames.DemoResetHotModule,
        StartupBackfills.StepNames.DemoAutumnCohort,
        StartupBackfills.StepNames.ReconcileEnrolledCount,
    ];

    [Fact]
    public async Task A_crash_right_after_step_six_resumes_cleanly_on_restart()
    {
        Assert.Equal(StartupBackfills.StepNames.EnrolmentWindows, DemoOnStepOrder[5]);

        // An isolated, far-future clock: any data_backfills row whose completed_at lands exactly on it was touched
        // by *this* call, never by the shared factory's own startup or by another test using the real clock.
        var clock = new FakeTimeProvider(new DateTimeOffset(2030, 1, 1, 0, 0, 0, TimeSpan.Zero));
        await using var host = factory.Derive(clock: clock);
        await using var scope = host.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<RushDayDbContext>();
        var options = PersistenceTestSupport.BackfillOptions(demoEnabled: true);

        var crashLogger = new CrashAfterStepLogger(StartupBackfills.StepNames.EnrolmentWindows);
        await Assert.ThrowsAsync<SimulatedCrashException>(() => StartupBackfills.RunAsync(db, options, clock, crashLogger));

        var afterCrash = await CompletedAtByStepAsync(db);
        var crashInstant = clock.GetUtcNow();
        Assert.Equal(crashInstant, afterCrash[StartupBackfills.StepNames.EnrolmentWindows]);

        // Each step commits its own transaction with its own data_backfills row: the crash, raised only from the
        // logger call that follows that commit, neither rolled step 6 back nor let step 7 (or any later step) start.
        Assert.NotEqual(crashInstant, afterCrash[StartupBackfills.StepNames.LecturersAndAssignments]);
        Assert.NotEqual(crashInstant, afterCrash[StartupBackfills.StepNames.DemoAccounts]);
        Assert.NotEqual(crashInstant, afterCrash[StartupBackfills.StepNames.ReconcileEnrolledCount]);

        // Restart: completes without throwing, and every step - including the ones the crash never reached - has run.
        clock.Advance(TimeSpan.FromHours(1));
        var restartInstant = clock.GetUtcNow();
        var resumed = await StartupBackfills.RunAsync(db, options, clock, NullLogger.Instance);

        foreach (var step in DemoOnStepOrder)
        {
            Assert.Contains(step, resumed.Select(r => r.Name));
        }

        var afterRestart = await CompletedAtByStepAsync(db);
        foreach (var step in DemoOnStepOrder)
        {
            Assert.True(afterRestart.ContainsKey(step), $"Missing data_backfills row for {step} after the restart.");
        }

        // "Always" steps run again on the restart and prove it reached all the way to the end this time.
        Assert.Equal(restartInstant, afterRestart[StartupBackfills.StepNames.EnrolmentWindows]);
        Assert.Equal(restartInstant, afterRestart[StartupBackfills.StepNames.DemoAccounts]);
        Assert.Equal(restartInstant, afterRestart[StartupBackfills.StepNames.ReconcileEnrolledCount]);

        // Restart-after-success: every step is idempotent by design, so one more run affects zero rows everywhere.
        clock.Advance(TimeSpan.FromMinutes(1));
        var steady = await StartupBackfills.RunAsync(db, options, clock, NullLogger.Instance);
        Assert.All(steady, r => Assert.Equal(0, r.RowsAffected));
    }

    private static Task<Dictionary<string, DateTimeOffset>> CompletedAtByStepAsync(RushDayDbContext db) =>
        db.DataBackfills.AsNoTracking().ToDictionaryAsync(b => b.Name, b => b.CompletedAt);

    /// <summary>Throws the first time it sees the named step's completion line, which <c>RunStepAsync</c> logs only after committing that step's transaction.</summary>
    private sealed class CrashAfterStepLogger(string stepName) : ILogger
    {
        private readonly string _prefix = "Backfill " + stepName + ":";
        private bool _thrown;

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            ArgumentNullException.ThrowIfNull(formatter);
            var message = formatter(state, exception);
            if (!_thrown && message.StartsWith(_prefix, StringComparison.Ordinal))
            {
                _thrown = true;
                throw new SimulatedCrashException(stepName);
            }
        }
    }

    private sealed class SimulatedCrashException(string stepName) : Exception($"Simulated crash right after step '{stepName}' committed.");
}
