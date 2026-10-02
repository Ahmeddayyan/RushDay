using RushDay.Domain.Enrolments;
using RushDay.Domain.Grades;
using RushDay.Domain.Modules;
using RushDay.Infrastructure.Caching;
using RushDay.Infrastructure.Enrolments;
using RushDay.Infrastructure.Grades;
using RushDay.Infrastructure.Queries;
using GradeClassification = RushDay.Domain.Grades.Classification;

namespace RushDay.Api.Contracts;

/// <summary>
/// <c>GradeResult</c> of 02-api.md section 7: a visible grade only (student contracts have no status field and nothing
/// that could hold a draft mark). <c>mark</c> is null iff <c>outcome</c> is not <c>mark</c>.
/// </summary>
public sealed record GradeResult(
    string ModuleCode,
    string ModuleTitle,
    int Credits,
    Semester Semester,
    string AcademicYear,
    GradeOutcome Outcome,
    int? Mark,
    DateTimeOffset PublishedAt,
    DateTimeOffset? CorrectedAt)
{
    public static GradeResult From(VisibleGradeRow grade)
    {
        ArgumentNullException.ThrowIfNull(grade);
        return new GradeResult(grade.ModuleCode, grade.ModuleTitle, grade.Credits, grade.Semester, grade.AcademicYear, grade.Outcome, grade.Mark, grade.PublishedAt, grade.CorrectedAt);
    }

    public static IReadOnlyList<GradeResult> From(IEnumerable<VisibleGradeRow> grades)
    {
        ArgumentNullException.ThrowIfNull(grades);
        return [.. grades.Select(From)];
    }
}

/// <summary><c>DashboardResponse.modules[]</c>: an active enrolment of the current academic year.</summary>
public sealed record DashboardModule(
    string Code,
    string Title,
    int Credits,
    Semester Semester,
    string AcademicYear,
    DateTimeOffset EnrolledAt,
    bool CanWithdraw,
    WithdrawBlock? WithdrawBlockedReason,
    DateTimeOffset? WithdrawalDeadlineAt);

/// <summary><c>DashboardResponse.completed[]</c>: an active enrolment of an earlier year; outcome, mark and band only when visible.</summary>
public sealed record CompletedModule(
    string Code,
    string Title,
    int Credits,
    Semester Semester,
    string AcademicYear,
    GradeOutcome? Outcome,
    int? Mark,
    string? Band);

/// <summary><c>DashboardResponse.credits</c>: the current year's active enrolments per semester.</summary>
public sealed record CreditSummary(int Autumn, int Spring, int Limit);

/// <summary><c>DashboardResponse</c> of 02-api.md section 8.3.</summary>
public sealed record DashboardResponse(
    string StudentNumber,
    string FullName,
    string Programme,
    int YearOfStudy,
    string AcademicYear,
    Semester CurrentSemester,
    IReadOnlyList<DashboardModule> Modules,
    IReadOnlyList<CompletedModule> Completed,
    IReadOnlyList<TimetableEntry> Timetable,
    IReadOnlyList<GradeResult> Results,
    double? WeightedAverage,
    string? Classification,
    CreditSummary Credits,
    PublicationBrief? NextPublication,
    PublicationBrief? LatestPublication,
    IReadOnlyList<WindowInfo> EnrolmentWindows,
    IReadOnlyList<AnnouncementView> Announcements)
{
    /// <summary>Assembles the five queries' data with the cached calendar, windows and publications.</summary>
    public static DashboardResponse From(
        DashboardData data,
        AcademicCalendar calendar,
        IReadOnlyList<EnrolmentWindowSnapshot> windows,
        PublicationBriefs publications,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(calendar);
        ArgumentNullException.ThrowIfNull(windows);
        ArgumentNullException.ThrowIfNull(publications);

        var year = calendar.AcademicYear;
        var modules = data.Current.Select(row =>
        {
            var withdrawal = WithdrawalEligibility.Evaluate(
                EnrolmentStatus.Active,
                row.AcademicYear,
                row.HasResult,
                year,
                EnrolmentWindowService.Find(windows, row.AcademicYear, row.Semester),
                now);
            return new DashboardModule(row.ModuleCode, row.Title, row.Credits, row.Semester, row.AcademicYear, row.EnrolledAt, withdrawal.CanWithdraw, withdrawal.BlockedReason, withdrawal.WithdrawalDeadlineAt);
        });

        var visibleByModule = data.Results.ToDictionary(r => r.ModuleId);
        var completed = data.Completed.Select(row =>
        {
            var grade = visibleByModule.GetValueOrDefault(row.ModuleId);
            var band = grade is { Outcome: GradeOutcome.Mark, Mark: { } mark } ? GradeClassification.Band(mark) : null;
            return new CompletedModule(row.ModuleCode, row.Title, row.Credits, row.Semester, row.AcademicYear, grade?.Outcome, grade?.Mark, band);
        });

        var (average, classification) = GradeQueries.Summarise(data.Results);
        var credits = new CreditSummary(
            data.Current.Where(r => r.Semester == Semester.Autumn).Sum(r => r.Credits),
            data.Current.Where(r => r.Semester == Semester.Spring).Sum(r => r.Credits),
            EnrolmentRules.MaxCreditsPerSemester);

        return new DashboardResponse(
            data.Student.StudentNumber,
            data.Student.FullName,
            data.Student.Programme,
            data.Student.YearOfStudy,
            year,
            calendar.CurrentSemester,
            [.. modules],
            [.. completed],
            TimetableEntry.From(data.Timetable),
            GradeResult.From(data.Results),
            average,
            classification,
            credits,
            PublicationBrief.From(publications.Next, now),
            PublicationBrief.From(publications.Latest, now),
            [.. windows.Where(w => string.Equals(w.AcademicYear, year, StringComparison.Ordinal)).OrderBy(w => w.Semester).Select(w => WindowInfo.From(w, now))],
            AnnouncementView.From(data.Announcements));
    }
}

/// <summary><c>results.semesters[]</c>: <c>publishAt</c> is set only for a scheduled group, whose marks are never sent.</summary>
public sealed record ResultsSemesterView(string AcademicYear, Semester Semester, ResultsState State, DateTimeOffset? PublishAt, IReadOnlyList<GradeResult> Results);

/// <summary><c>GET /api/me/results</c>: newest year first, autumn before spring.</summary>
public sealed record ResultsResponse(IReadOnlyList<ResultsSemesterView> Semesters, double? WeightedAverage, string? Classification)
{
    public static ResultsResponse From(ResultsData data)
    {
        ArgumentNullException.ThrowIfNull(data);
        return new ResultsResponse(
            [.. data.Semesters.Select(s => new ResultsSemesterView(s.AcademicYear, s.Semester, s.State, s.PublishAt, GradeResult.From(s.Results)))],
            data.WeightedAverage,
            data.Classification);
    }
}
