namespace RushDay.Domain.Modules;

/// <summary>A taught module with a fixed number of places.</summary>
public sealed class Module
{
    public Guid Id { get; init; }
    public required string Code { get; init; }
    public required string Title { get; init; }
    public int Credits { get; init; }
    public int Capacity { get; init; }
    public Semester Semester { get; init; }
}
