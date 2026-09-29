using RushDay.Domain.Enrolments;
using RushDay.Domain.Modules;
using RushDay.Infrastructure.Caching;
using RushDay.Infrastructure.Seeding;

namespace RushDay.Infrastructure.Enrolments;

/// <summary>The settings year and the semester being taught now (<c>academic_settings</c>, cached).</summary>
public sealed record AcademicCalendar(string AcademicYear, Semester CurrentSemester);

/// <summary><c>ModuleSummary.enrolmentState</c>: the state of the window for (current year, module semester) at request time.</summary>
public enum EnrolmentState
{
    NotYetOpen,
    Open,
    Closed,
    NoWindow,
}

/// <summary><c>MyEnrolment.withdrawBlockedReason</c>: the first failing condition of <c>canWithdraw</c> (02-api.md section 7).</summary>
public enum WithdrawBlock
{
    Year,
    Deadline,
    Results,
}

/// <summary>The window of a module's semester in the current year, evaluated at one instant.</summary>
public sealed record ModuleWindow(EnrolmentState State, DateTimeOffset? OpensAt, DateTimeOffset? ClosesAt, DateTimeOffset? WithdrawalDeadlineAt)
{
    public static ModuleWindow Of(EnrolmentWindowSnapshot? window, DateTimeOffset now)
    {
        if (window is null)
        {
            return new ModuleWindow(EnrolmentState.NoWindow, null, null, null);
        }

        var state = now < window.OpensAt ? EnrolmentState.NotYetOpen : window.IsOpenAt(now) ? EnrolmentState.Open : EnrolmentState.Closed;
        return new ModuleWindow(state, window.OpensAt, window.ClosesAt, window.WithdrawalDeadlineAt);
    }
}

/// <summary>Whether an enrolment row can be withdrawn by its student now, and why not.</summary>
public sealed record WithdrawalEligibility(bool CanWithdraw, WithdrawBlock? BlockedReason, DateTimeOffset? WithdrawalDeadlineAt)
{
    /// <summary>
    /// <c>canWithdraw = status = 'active' AND academicYear = current year AND now &lt; withdrawal_deadline_at AND no
    /// Submitted or Published grade</c>; the reason names the first failing condition (year, deadline, results) and is
    /// null when the row can be withdrawn or is already withdrawn (02-api.md section 7). A row without a window has no
    /// deadline to meet, so it fails on <c>deadline</c>, which is what <c>WithdrawAsync</c> answers
    /// (<c>withdrawal-deadline-passed</c>). <paramref name="window"/> is the window of the row's own year and semester.
    /// </summary>
    public static WithdrawalEligibility Evaluate(
        EnrolmentStatus status,
        string academicYear,
        bool hasResult,
        string currentYear,
        EnrolmentWindowSnapshot? window,
        DateTimeOffset now)
    {
        var deadline = window?.WithdrawalDeadlineAt;
        if (status != EnrolmentStatus.Active)
        {
            return new WithdrawalEligibility(false, null, deadline);
        }

        if (!string.Equals(academicYear, currentYear, StringComparison.Ordinal))
        {
            return new WithdrawalEligibility(false, WithdrawBlock.Year, deadline);
        }

        if (window is null || !window.AllowsWithdrawalAt(now))
        {
            return new WithdrawalEligibility(false, WithdrawBlock.Deadline, deadline);
        }

        return hasResult
            ? new WithdrawalEligibility(false, WithdrawBlock.Results, deadline)
            : new WithdrawalEligibility(true, null, deadline);
    }
}

/// <summary>
/// The read side of enrolment windows for the student surface: the current academic calendar from <c>settings</c> and
/// the windows from <c>windows:all</c>, both cached, so a request pays no query for them. Window mutations are S6's
/// <c>EnrolmentWindowAdminService</c>.
/// </summary>
public sealed class EnrolmentWindowService(SettingsCache settings, EnrolmentWindowCache windows)
{
    /// <summary>
    /// The settings year and current semester. Before the backfills have created the settings row the demo year and
    /// autumn stand in, as on <c>GET /api/public/status</c>.
    /// </summary>
    public async ValueTask<AcademicCalendar> CurrentAsync(CancellationToken cancellationToken = default)
    {
        var row = await settings.GetAsync(cancellationToken);
        return new AcademicCalendar(row?.AcademicYear ?? StartupBackfills.CurrentAcademicYear, row?.CurrentSemester ?? Semester.Autumn);
    }

    /// <summary>Every window of every year (cached), for lookups by (year, semester) in memory.</summary>
    public ValueTask<IReadOnlyList<EnrolmentWindowSnapshot>> AllAsync(CancellationToken cancellationToken = default) =>
        windows.GetAllAsync(cancellationToken);

    /// <summary>The windows of one academic year, autumn first.</summary>
    public ValueTask<IReadOnlyList<EnrolmentWindowSnapshot>> ForYearAsync(string academicYear, CancellationToken cancellationToken = default) =>
        windows.ForYearAsync(academicYear, cancellationToken);

    public ValueTask<EnrolmentWindowSnapshot?> FindAsync(string academicYear, Semester semester, CancellationToken cancellationToken = default) =>
        windows.FindAsync(academicYear, semester, cancellationToken);

    /// <summary>The window of (year, semester) from an already-read list, or null.</summary>
    public static EnrolmentWindowSnapshot? Find(IEnumerable<EnrolmentWindowSnapshot> all, string academicYear, Semester semester)
    {
        ArgumentNullException.ThrowIfNull(all);
        return all.FirstOrDefault(w => w.Semester == semester && string.Equals(w.AcademicYear, academicYear, StringComparison.Ordinal));
    }
}
