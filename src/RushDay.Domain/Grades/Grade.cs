namespace RushDay.Domain.Grades;

/// <summary>
/// A mark for one student on one module. Visible to the student only when
/// <see cref="Status"/> is Published and <see cref="PublishedAt"/> has passed.
/// </summary>
public sealed class Grade
{
    public Guid Id { get; init; }
    public Guid StudentId { get; init; }
    public Guid ModuleId { get; init; }

    /// <summary>Null iff <see cref="Outcome"/> is not <see cref="GradeOutcome.Mark"/> (CHECK ck_grades_mark_outcome).</summary>
    public int? Mark { get; set; }

    public GradeOutcome Outcome { get; set; }
    public GradeStatus Status { get; set; }
    public DateTimeOffset? PublishedAt { get; set; }
    public Guid? PublicationId { get; set; }
    public Guid? EnteredByUserId { get; set; }
    public DateTimeOffset? SubmittedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>Incremented on every mark, outcome or status change; the marks grid sends it back for optimistic concurrency.</summary>
    public int Version { get; set; }

    /// <summary>Set by the administrator's correction route; shown to the student as "Amended".</summary>
    public DateTimeOffset? CorrectedAt { get; set; }
}
