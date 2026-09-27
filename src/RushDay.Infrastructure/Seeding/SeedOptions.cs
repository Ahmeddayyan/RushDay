namespace RushDay.Infrastructure.Seeding;

public sealed record SeedOptions
{
    /// <summary>Default sized for a mid-size UK university cohort hitting the portal on results day.</summary>
    public int StudentCount { get; init; } = 20_000;

    public int ModulesPerLevelPerSemester { get; init; } = 20;

    public int AutumnModulesPerStudent { get; init; } = 4;

    /// <summary>Deterministic seed so every environment gets the same data.</summary>
    public int RandomSeed { get; init; } = 42;

    /// <summary>The moment autumn results are published: the spike everyone is waiting for.</summary>
    public DateTimeOffset ResultsDay { get; init; } = new(2026, 9, 28, 9, 0, 0, TimeSpan.Zero);
}
