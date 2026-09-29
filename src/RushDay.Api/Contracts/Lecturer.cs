using System.ComponentModel.DataAnnotations;
using RushDay.Domain.Enrolments;
using RushDay.Domain.Grades;
using RushDay.Domain.Modules;
using RushDay.Infrastructure.Announcements;
using RushDay.Infrastructure.Grades;
using RushDay.Infrastructure.Queries;

namespace RushDay.Api.Contracts;

/// <summary>
/// The patterns of the staff surface's route constraints and request validators, with explicit ASCII classes: .NET's
/// <c>\d</c> matches any Unicode digit (Arabic-Indic, full-width), which would reach lookups and response headers.
/// Route constraints match case-insensitively, so handlers upper-case codes before lookup and answer with the
/// stored, canonical value.
/// </summary>
public static class StaffPatterns
{
    public const string ModuleCodeRoute = "{code:regex(^[A-Z]{{2}}[0-9]{{4}}$)}";
    public const string StudentNumberRoute = "{studentNumber:regex(^S[0-9]{{6}}$)}";
    public const string StaffNumberRoute = "{staffNumber:regex(^L[0-9]{{5}}$)}";

    /// <summary>A module code in a body; upper-cased before lookup.</summary>
    public const string ModuleCode = "^[A-Za-z]{2}[0-9]{4}$";

    /// <summary>A student number in a body; upper-cased before lookup.</summary>
    public const string StudentNumber = "^[Ss][0-9]{6}$";

    /// <summary>A staff number in a body; upper-cased before lookup.</summary>
    public const string StaffNumber = "^[Ll][0-9]{5}$";

    /// <summary><c>2026/27</c>.</summary>
    public const string AcademicYear = "^[0-9]{4}/[0-9]{2}$";

    /// <summary><c>autumn</c> or <c>spring</c>, case-insensitively, never a number (the <c>SemesterQuery</c> rule of 02-api.md section 1).</summary>
    public const string SemesterName = "^([Aa][Uu][Tt][Uu][Mm][Nn]|[Ss][Pp][Rr][Ii][Nn][Gg])$";

    public const int ReasonMinLength = 10;
    public const int ReasonMaxLength = 400;
    public const int SearchMaxLength = 100;

    /// <summary>
    /// 400 <c>validation</c> for a page that ends beyond row <see cref="PageRequest.MaxRows"/> (review S6 E14), with the
    /// field error on <c>page</c>.
    /// </summary>
    public static IResult PageTooDeep() => Security.ProblemResults.Problem(
        Security.ProblemTypes.Validation,
        $"Pages stop at row {PageRequest.MaxRows:N0}; narrow the search or use the export.",
        new Dictionary<string, object?> { ["errors"] = new Dictionary<string, string[]> { ["page"] = [$"page x pageSize may not exceed {PageRequest.MaxRows}."] } });

    public static Semester? ParseSemester(string? value) =>
        string.Equals(value, "autumn", StringComparison.OrdinalIgnoreCase) ? RushDay.Domain.Modules.Semester.Autumn
        : string.Equals(value, "spring", StringComparison.OrdinalIgnoreCase) ? RushDay.Domain.Modules.Semester.Spring
        : null;
}

/// <summary>
/// Rejects text containing U+0000, which PostgreSQL cannot store in <c>text</c> or <c>varchar</c> (it would answer a
/// 500 instead of the 400 <c>validation</c> the caller deserves).
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Parameter | AttributeTargets.Field)]
public sealed class NoNulAttribute : ValidationAttribute
{
    public NoNulAttribute()
        : base("The field must not contain a NUL character.")
    {
    }

    public override bool IsValid(object? value) => value is not string text || !text.Contains('\0', StringComparison.Ordinal);
}

/// <summary>
/// Rejects a JSON array with a <c>null</c> element (<c>{"rows":[null]}</c>): minimal API validation skips a null item,
/// and the handler would then dereference it, a 500 instead of the 400 <c>validation</c> the caller deserves (review
/// S6 E11).
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Parameter | AttributeTargets.Field)]
public sealed class NoNullElementsAttribute : ValidationAttribute
{
    public NoNullElementsAttribute()
        : base("The list must not contain null.")
    {
    }

    public override bool IsValid(object? value) =>
        value is not System.Collections.IEnumerable items || items.Cast<object?>().All(item => item is not null);
}

/// <summary><c>MarksStatus</c> of 02-api.md section 7.</summary>
public sealed record MarksStatus(MarksState Status, int Entered, int Missing, int Total, DateTimeOffset? SubmittedAt, DateTimeOffset? PublishedAt)
{
    public static MarksStatus From(MarksStatusData data)
    {
        ArgumentNullException.ThrowIfNull(data);
        return new MarksStatus(data.Status, data.Entered, data.Missing, data.Total, data.SubmittedAt, data.PublishedAt);
    }
}

/// <summary><c>GET /api/lecturer/modules</c>: <c>ModuleSummary &amp; { myRole, marks }</c>.</summary>
public sealed record LecturerModuleView : ModuleSummary
{
    public LecturerModuleView(ModuleSummary summary, ModuleLecturerRole myRole, MarksStatus marks)
        : base(summary)
    {
        MyRole = myRole;
        Marks = marks;
    }

    public ModuleLecturerRole MyRole { get; init; }

    public MarksStatus Marks { get; init; }
}

/// <summary>One roster line.</summary>
public sealed record RosterItem(
    string StudentNumber,
    string FullName,
    string Programme,
    int YearOfStudy,
    EnrolmentStatus Status,
    DateTimeOffset EnrolledAt,
    DateTimeOffset? WithdrawnAt)
{
    public static RosterItem From(RosterRow row)
    {
        ArgumentNullException.ThrowIfNull(row);
        return new RosterItem(row.StudentNumber, row.FullName, row.Programme, row.YearOfStudy, row.Status, row.EnrolledAt, row.WithdrawnAt);
    }
}

/// <summary>The roster of 02-api.md section 8.4 (lecturer and administrator alike).</summary>
public sealed record RosterResponse(ModuleSummary Module, IReadOnlyList<RosterItem> Items, int Page, int PageSize, int Total)
{
    public static RosterResponse From(ModuleSummary module, PagedResult<RosterRow> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);
        return new RosterResponse(module, [.. rows.Items.Select(RosterItem.From)], rows.Page, rows.PageSize, rows.Total);
    }
}

/// <summary><c>MarksSheet.summary</c>: the whole module, whatever the page.</summary>
public sealed record MarksSummary(int Entered, int Missing, int Total)
{
    public static MarksSummary From(MarksStatusData status)
    {
        ArgumentNullException.ThrowIfNull(status);
        return new MarksSummary(status.Entered, status.Missing, status.Total);
    }
}

/// <summary><c>MarksSheet.rows[]</c>.</summary>
public sealed record MarksSheetRow(
    string StudentNumber,
    string FullName,
    EnrolmentStatus EnrolmentStatus,
    GradeOutcome? Outcome,
    int? Mark,
    GradeStatus? GradeStatus,
    int? Version,
    DateTimeOffset? UpdatedAt,
    string? EnteredBy,
    DateTimeOffset? CorrectedAt)
{
    public static MarksSheetRow From(MarksRowData row)
    {
        ArgumentNullException.ThrowIfNull(row);
        return new MarksSheetRow(row.StudentNumber, row.FullName, row.EnrolmentStatus, row.Outcome, row.Mark, row.GradeStatus, row.Version, row.UpdatedAt, row.EnteredBy, row.CorrectedAt);
    }
}

/// <summary><c>MarksSheet</c> of 02-api.md section 8.4 (<c>myRole</c> is null on the administrator's read route).</summary>
public sealed record MarksSheet(
    string Code,
    string Title,
    string AcademicYear,
    MarksState Status,
    DateTimeOffset? SubmittedAt,
    DateTimeOffset? PublishedAt,
    ModuleLecturerRole? MyRole,
    string? Leader,
    MarksSummary Summary,
    IReadOnlyList<MarksSheetRow> Rows,
    int Page,
    int PageSize,
    int Total)
{
    public static MarksSheet From(MarksSheetData data)
    {
        ArgumentNullException.ThrowIfNull(data);
        return new MarksSheet(
            data.Code,
            data.Title,
            data.AcademicYear,
            data.Status.Status,
            data.Status.SubmittedAt,
            data.Status.PublishedAt,
            data.MyRole,
            data.Leader,
            MarksSummary.From(data.Status),
            [.. data.Rows.Items.Select(MarksSheetRow.From)],
            data.Rows.Page,
            data.Rows.PageSize,
            data.Rows.Total);
    }
}

/// <summary>Query parameters of the lecturer roster: <c>q</c> (≤ 100), <c>page</c>, <c>pageSize</c> (clamped to 200).</summary>
public sealed record RosterParameters
{
    [StringLength(StaffPatterns.SearchMaxLength)]
    [NoNul]
    public string? Q { get; init; }

    public int? Page { get; init; }

    public int? PageSize { get; init; }
}

/// <summary>One row of <c>PUT /api/lecturer/modules/{code}/marks</c>.</summary>
public sealed record MarkRowRequest : IValidatableObject
{
    [Required]
    [RegularExpression(StaffPatterns.StudentNumber)]
    public string? StudentNumber { get; init; }

    [Range(0, 100)]
    public int? Mark { get; init; }

    /// <summary>Defaults to <c>mark</c>.</summary>
    [EnumDataType(typeof(GradeOutcome))]
    public GradeOutcome? Outcome { get; init; }

    /// <summary>The stored version the client saw; null for a row it saw empty.</summary>
    [Range(1, int.MaxValue)]
    public int? Version { get; init; }

    public GradeOutcome EffectiveOutcome => Outcome ?? GradeOutcome.Mark;

    /// <summary><c>mark</c> is required (0..100) when the outcome is a mark and must be null otherwise.</summary>
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (EffectiveOutcome == GradeOutcome.Mark && Mark is null)
        {
            yield return new ValidationResult("A mark is required when the outcome is mark.", [nameof(Mark)]);
        }
        else if (EffectiveOutcome != GradeOutcome.Mark && Mark is not null)
        {
            yield return new ValidationResult("The mark must be null when the outcome is absent or deferred.", [nameof(Mark)]);
        }
    }

    public MarkEntry ToEntry() => new(StudentNumber!, EffectiveOutcome == GradeOutcome.Mark ? Mark : null, EffectiveOutcome, Version);
}

/// <summary><c>PUT /api/lecturer/modules/{code}/marks</c>: 1..500 rows, each student at most once.</summary>
public sealed record SaveMarksRequest : IValidatableObject
{
    [Required]
    [MinLength(1)]
    [MaxLength(MarksService.MaxRowsPerRequest)]
    [NoNullElements]
    public List<MarkRowRequest>? Rows { get; init; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        var duplicates = (Rows ?? [])
            .Where(r => r?.StudentNumber is not null)
            .GroupBy(r => r.StudentNumber!.ToUpperInvariant(), StringComparer.Ordinal)
            .Any(g => g.Count() > 1);
        if (duplicates)
        {
            yield return new ValidationResult("Each student may appear at most once per request.", [nameof(Rows)]);
        }
    }
}

/// <summary>The answer of a save: the whole-module summary and exactly the submitted rows as stored, in request order.</summary>
public sealed record SaveMarksResponse(MarksSummary Summary, IReadOnlyList<MarksSheetRow> Rows);

/// <summary><c>POST /api/lecturer/modules/{code}/marks/submit</c>.</summary>
public sealed record SubmitMarksResponse(string Code, MarksState Status, DateTimeOffset SubmittedAt, int GradeCount);

/// <summary>The body of every announcement create and update (lecturer module routes and administrator routes).</summary>
public sealed record AnnouncementRequest : IValidatableObject
{
    [Required]
    [StringLength(120, MinimumLength = 1)]
    [NoNul]
    public string? Title { get; init; }

    [Required]
    [StringLength(4000, MinimumLength = 1)]
    [NoNul]
    public string? Body { get; init; }

    public bool? Pinned { get; init; }

    public DateTimeOffset? PublishedAt { get; init; }

    public DateTimeOffset? ExpiresAt { get; init; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (PublishedAt is { } published && ExpiresAt is { } expires && expires <= published)
        {
            yield return new ValidationResult("The announcement must expire after it is published.", [nameof(ExpiresAt)]);
        }
    }

    public AnnouncementDraft ToDraft() => new(Title!.Trim(), Body!, Pinned ?? false, PublishedAt, ExpiresAt);
}
