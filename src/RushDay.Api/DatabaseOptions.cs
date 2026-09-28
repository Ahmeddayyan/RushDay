namespace RushDay.Api;

/// <summary>
/// Startup database behaviour, bound from the "Database" configuration section.
/// Locally we run migrations explicitly; on the free cloud tier there is no shell, so the app does it on boot.
/// </summary>
public sealed record DatabaseOptions
{
    public const string SectionName = "Database";

    public bool MigrateOnStartup { get; init; }

    public bool SeedOnStartup { get; init; }

    /// <summary>Run the idempotent startup backfills after seeding. Always on under --migrate-and-seed.</summary>
    public bool BackfillOnStartup { get; init; }

    public int SeedStudentCount { get; init; } = 20_000;

    /// <summary>The instant the seeded autumn results publish; tests set it in the past so results are visible.</summary>
    public DateTimeOffset SeedResultsDay { get; init; } = new(2026, 9, 28, 9, 0, 0, TimeSpan.Zero);

    /// <summary>Npgsql Maximum Pool Size: 20 in Production (Neon free tier), 40 locally for load runs.</summary>
    public int MaxPoolSize { get; init; } = 20;
}
