namespace RushDay.Api.Options;

/// <summary>
/// Startup database behaviour, bound from the "Database" section (03-security.md section 8). Locally the scripts
/// migrate explicitly (<c>--migrate-and-seed</c>); on Render there is no shell, so the app migrates and backfills on boot.
/// </summary>
public sealed record DatabaseOptions
{
    public const string SectionName = "Database";

    public bool MigrateOnStartup { get; init; }

    /// <summary>Honoured only when <c>Demo:Enabled</c> is true (D29); <c>--migrate-and-seed</c> seeds regardless.</summary>
    public bool SeedOnStartup { get; init; }

    /// <summary>Run the idempotent startup backfills. Always on under <c>--migrate-and-seed</c>.</summary>
    public bool BackfillOnStartup { get; init; }

    public int SeedStudentCount { get; init; } = 20_000;

    /// <summary>The instant the seeded autumn results publish; tests set it in the past so results are visible (D24).</summary>
    public DateTimeOffset SeedResultsDay { get; init; } = new(2026, 9, 28, 9, 0, 0, TimeSpan.Zero);

    /// <summary>Npgsql Maximum Pool Size: 20 in Production (Neon free tier), 40 locally for load runs (D14).</summary>
    public int MaxPoolSize { get; init; } = 20;
}
