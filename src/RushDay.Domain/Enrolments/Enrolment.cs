namespace RushDay.Domain.Enrolments;

public sealed class Enrolment
{
    public Guid Id { get; init; }
    public Guid StudentId { get; init; }
    public Guid ModuleId { get; init; }
    public DateTimeOffset EnrolledAt { get; init; }
}
