namespace RushDay.Domain.Modules;

/// <summary>A taught module with a fixed number of places.</summary>
public sealed class Module
{
    public Guid Id { get; init; }
    public required string Code { get; init; }
    public required string Department { get; init; }
    public required string Title { get; set; }
    public string? Description { get; set; }
    public int Credits { get; set; }
    public int Capacity { get; set; }
    public Semester Semester { get; set; }

    /// <summary>Number of enrolments with status Active, maintained by the enrolment transaction.</summary>
    public int EnrolledCount { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTimeOffset? UpdatedAt { get; set; }

    /// <summary>The level is the third character of the code: CS3099 is a level-3 module.</summary>
    public int Level => Code[2] - '0';
}
