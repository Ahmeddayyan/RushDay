namespace RushDay.Domain.Enrolments;

/// <summary>
/// A student's place on a module. Rows are never deleted: withdrawing toggles <see cref="Status"/> and
/// re-enrolling reactivates the same row, so the unique (student, module) index keeps duplicates impossible.
/// </summary>
public sealed class Enrolment
{
    public Guid Id { get; init; }
    public Guid StudentId { get; init; }
    public Guid ModuleId { get; init; }
    public DateTimeOffset EnrolledAt { get; set; }
    public EnrolmentStatus Status { get; set; }
    public EnrolmentSource Source { get; set; }
    public DateTimeOffset? WithdrawnAt { get; set; }

    /// <summary>Null for seed rows and for self-enrolments; the audit row names the actor.</summary>
    public Guid? CreatedByUserId { get; set; }

    public DateTimeOffset? UpdatedAt { get; set; }
}
