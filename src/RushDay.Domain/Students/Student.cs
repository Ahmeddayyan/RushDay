namespace RushDay.Domain.Students;

/// <summary>A registered student, identified externally by student number (e.g. S000123).</summary>
public sealed class Student
{
    public Guid Id { get; init; }
    public required string StudentNumber { get; init; }
    public required string FullName { get; set; }
    public required string Programme { get; set; }
    public int YearOfStudy { get; set; }
    public string? Email { get; set; }

    /// <summary>Set when an administrator marks the student as having left; such a student cannot be enrolled by anyone.</summary>
    public DateTimeOffset? LeftAt { get; set; }
}
