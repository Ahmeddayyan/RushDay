namespace RushDay.Domain.Grades;

public sealed class Grade
{
    public Guid Id { get; init; }
    public Guid StudentId { get; init; }
    public Guid ModuleId { get; init; }
    public int Mark { get; init; }
    public DateTimeOffset PublishedAt { get; init; }
}
