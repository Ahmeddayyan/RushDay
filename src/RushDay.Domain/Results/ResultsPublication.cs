using RushDay.Domain.Modules;

namespace RushDay.Domain.Results;

/// <summary>
/// An administrator's decision to release a semester's submitted marks at a stored instant.
/// State is derived by reads: scheduled while <see cref="PublishAt"/> is in the future, otherwise live.
/// </summary>
public sealed class ResultsPublication
{
    public Guid Id { get; init; }
    public required string AcademicYear { get; init; }
    public Semester Semester { get; init; }
    public DateTimeOffset PublishAt { get; set; }
    public DateTimeOffset CreatedAt { get; init; }

    /// <summary>Null when the publication came from the seed.</summary>
    public Guid? CreatedByUserId { get; init; }

    public int GradeCount { get; set; }
    public int ModuleCount { get; set; }
    public string? Note { get; set; }

    /// <summary>
    /// The pinned "results are available" announcement a publish with <c>announce</c> posted, which follows the
    /// publication: a reschedule moves it, and a cancel, an unpublish or a return to draft that empties the publication
    /// deletes it. Null when the publish did not announce (and for the seed).
    /// </summary>
    public Guid? AnnouncementId { get; set; }
}
