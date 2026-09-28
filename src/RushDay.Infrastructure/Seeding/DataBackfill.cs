namespace RushDay.Infrastructure.Seeding;

/// <summary>One row per startup backfill step (table <c>data_backfills</c>); shown on the admin ops page.</summary>
public sealed class DataBackfill
{
    public required string Name { get; init; }
    public DateTimeOffset CompletedAt { get; set; }
    public int RowsAffected { get; set; }
    public string? Notes { get; set; }
}
