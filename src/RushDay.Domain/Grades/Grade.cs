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
    public int Mark { get; set; }
    public GradeStatus Status { get; set; }
    public DateTimeOffset? PublishedAt { get; set; }
    public Guid? PublicationId { get; set; }
    public Guid? EnteredByUserId { get; set; }
    public DateTimeOffset? SubmittedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>Incremented on every mark change; the marks grid sends it back for optimistic concurrency.</summary>
    public int Version { get; set; }
}
