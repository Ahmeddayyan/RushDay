using System.ComponentModel.DataAnnotations;
using RushDay.Domain.Grades;
using RushDay.Domain.Modules;
using RushDay.Infrastructure.Grades;
using RushDay.Infrastructure.Queries;

namespace RushDay.Api.Contracts;

/// <summary><c>PublicationInfo = PublicationBrief &amp; { id, gradeCount, moduleCount, createdAt, createdBy, note }</c>.</summary>
public sealed record PublicationInfo(
    string AcademicYear,
    Semester Semester,
    DateTimeOffset PublishAt,
    PublicationState State,
    Guid Id,
    int GradeCount,
    int ModuleCount,
    DateTimeOffset CreatedAt,
    string? CreatedBy,
    string? Note)
{
    public static PublicationInfo From(PublicationRecord publication, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(publication);
        return new PublicationInfo(
            publication.AcademicYear,
            publication.Semester,
            publication.PublishAt,
            publication.PublishAt > now ? PublicationState.Scheduled : PublicationState.Live,
            publication.Id,
            publication.GradeCount,
            publication.ModuleCount,
            publication.CreatedAt,
            publication.CreatedBy,
            publication.Note);
    }
}

/// <summary>
/// The query of <c>GET /api/admin/results</c>: <c>semester</c> (required, <c>autumn</c> or <c>spring</c>
/// case-insensitively, never a number) and <c>academicYear</c> (defaults to the settings year).
/// </summary>
public sealed record SemesterQuery
{
    [Required]
    [RegularExpression(StaffPatterns.SemesterName)]
    public string? Semester { get; init; }

    [RegularExpression(StaffPatterns.AcademicYear)]
    public string? AcademicYear { get; init; }
}

/// <summary>One module of the submission progress table; <c>enrolledCount</c> = <c>marks.total</c>.</summary>
public sealed record AdminResultsModuleView(string Code, string Title, string? Leader, int EnrolledCount, MarksStatus Marks);

/// <summary><c>GET /api/admin/results</c>.</summary>
public sealed record AdminResultsResponse(string AcademicYear, Semester Semester, IReadOnlyList<AdminResultsModuleView> Modules, IReadOnlyList<PublicationInfo> Publications)
{
    public static AdminResultsResponse From(AdminResultsData data, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(data);
        return new AdminResultsResponse(
            data.AcademicYear,
            data.Semester,
            [.. data.Modules.Select(m => new AdminResultsModuleView(m.Code, m.Title, m.Leader, m.Marks.Total, MarksStatus.From(m.Marks)))],
            [.. data.Publications.Select(p => PublicationInfo.From(p, now))]);
    }
}

/// <summary><c>POST /api/admin/results/publish</c>.</summary>
public sealed record PublishRequest
{
    [Required]
    [RegularExpression(StaffPatterns.AcademicYear)]
    public string? AcademicYear { get; init; }

    [Required]
    [EnumDataType(typeof(Semester))]
    public Semester? Semester { get; init; }

    /// <summary>An earlier instant means now; at most 90 days ahead.</summary>
    [Required]
    public DateTimeOffset? PublishAt { get; init; }

    [Required]
    public bool? Announce { get; init; }

    [StringLength(400)]
    [NoNul]
    public string? Note { get; init; }
}

public sealed record PublishedCounts(int Modules, int Grades);

/// <summary><c>excluded[]</c>: a draft module (<c>notSubmitted</c>) or a submitted one with marks missing (<c>marksMissing</c>).</summary>
public sealed record ExcludedModuleView(string Code, MarksState Status, ExclusionReason Reason, int MarksMissing);

/// <summary>The answer of a publish.</summary>
public sealed record PublishResponse(PublicationInfo Publication, PublishedCounts Published, IReadOnlyList<ExcludedModuleView> Excluded)
{
    public static PublishResponse From(PublishOutcome outcome, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(outcome);
        return new PublishResponse(
            PublicationInfo.From(outcome.Publication, now),
            new PublishedCounts(outcome.Modules, outcome.Grades),
            [.. outcome.Excluded.Select(e => new ExcludedModuleView(e.Code, e.Status, e.Reason, e.MarksMissing))]);
    }
}

/// <summary><c>PUT /api/admin/results/publications/{id}</c>.</summary>
public sealed record ReschedulePublicationRequest
{
    [Required]
    public DateTimeOffset? PublishAt { get; init; }
}

/// <summary>What a cancel or an unpublish sent back to Submitted.</summary>
public sealed record RevertedPublicationResponse(string AcademicYear, Semester Semester, int Grades)
{
    public static RevertedPublicationResponse From(RevertedPublication reverted)
    {
        ArgumentNullException.ThrowIfNull(reverted);
        return new RevertedPublicationResponse(reverted.AcademicYear, reverted.Semester, reverted.Grades);
    }
}

/// <summary><c>POST /api/admin/results/modules/{code}/return-to-draft</c>.</summary>
public sealed record ReturnToDraftRequest
{
    [Required]
    [StringLength(StaffPatterns.ReasonMaxLength, MinimumLength = StaffPatterns.ReasonMinLength)]
    [NoNul]
    public string? Reason { get; init; }

    [RegularExpression(StaffPatterns.AcademicYear)]
    public string? AcademicYear { get; init; }
}

public sealed record ReturnToDraftResponse(string Code, MarksState Status, bool FromScheduledPublication);

/// <summary><c>POST /api/admin/results/modules/{code}/marks/{studentNumber}/correct</c>.</summary>
public sealed record CorrectMarkRequest : IValidatableObject
{
    /// <summary>Defaults to <c>mark</c>.</summary>
    [EnumDataType(typeof(GradeOutcome))]
    public GradeOutcome? Outcome { get; init; }

    [Range(0, 100)]
    public int? Mark { get; init; }

    [Required]
    [StringLength(StaffPatterns.ReasonMaxLength, MinimumLength = StaffPatterns.ReasonMinLength)]
    [NoNul]
    public string? Reason { get; init; }

    public GradeOutcome EffectiveOutcome => Outcome ?? GradeOutcome.Mark;

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
}

/// <summary><c>{ mark, outcome }</c> of a correction's before and after.</summary>
public sealed record MarkValueView(int? Mark, GradeOutcome Outcome)
{
    public static MarkValueView From(GradeValue value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return new MarkValueView(value.Mark, value.Outcome);
    }
}

public sealed record CorrectMarkResponse(string StudentNumber, MarkValueView Before, MarkValueView After, int Version, DateTimeOffset CorrectedAt)
{
    public static CorrectMarkResponse From(CorrectedGrade corrected)
    {
        ArgumentNullException.ThrowIfNull(corrected);
        return new CorrectMarkResponse(corrected.StudentNumber, MarkValueView.From(corrected.Before), MarkValueView.From(corrected.After), corrected.Version, corrected.CorrectedAt);
    }
}
