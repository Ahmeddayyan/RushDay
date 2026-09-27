namespace RushDay.Domain.Students;

/// <summary>A registered student, identified externally by student number (e.g. S000123).</summary>
public sealed class Student
{
    public Guid Id { get; init; }
    public required string StudentNumber { get; init; }
    public required string FullName { get; init; }
    public required string Programme { get; init; }
    public int YearOfStudy { get; init; }
}
