namespace RushDay.Domain.Lecturers;

/// <summary>A member of teaching staff, identified externally by staff number (e.g. L00001).</summary>
public sealed class Lecturer
{
    public Guid Id { get; init; }
    public required string StaffNumber { get; init; }
    public required string FullName { get; set; }
    public required string Title { get; set; }
    public required string Department { get; set; }
    public string? Email { get; set; }

    /// <summary>Set when an administrator marks the lecturer as having left; existing assignments stay, new ones are refused.</summary>
    public DateTimeOffset? LeftAt { get; set; }
}
