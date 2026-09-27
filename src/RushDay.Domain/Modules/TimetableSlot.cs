namespace RushDay.Domain.Modules;

public sealed class TimetableSlot
{
    public Guid Id { get; init; }
    public Guid ModuleId { get; init; }
    public DayOfWeek Day { get; init; }
    public TimeOnly StartTime { get; init; }
    public TimeOnly EndTime { get; init; }
    public required string Room { get; init; }
}
