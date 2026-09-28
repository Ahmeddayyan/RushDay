namespace RushDay.Api.Contracts;

public sealed record DashboardResponse(
    string StudentNumber,
    string FullName,
    string Programme,
    int YearOfStudy,
    IReadOnlyList<EnrolledModule> Modules,
    IReadOnlyList<TimetableEntry> Timetable,
    IReadOnlyList<GradeResult> Results,
    double? WeightedAverage,
    string? Classification);

public sealed record EnrolledModule(string Code, string Title, int Credits, string Semester);

public sealed record TimetableEntry(string ModuleCode, string Day, string StartTime, string EndTime, string Room);

public sealed record GradeResult(string ModuleCode, string ModuleTitle, int? Mark, DateTimeOffset? PublishedAt);
