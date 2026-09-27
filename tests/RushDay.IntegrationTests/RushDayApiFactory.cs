using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RushDay.Infrastructure.Persistence;
using RushDay.Infrastructure.Seeding;
using Testcontainers.PostgreSql;

namespace RushDay.IntegrationTests;

/// <summary>
/// Hosts the real API against a throwaway PostgreSQL container, migrated and seeded with a small cohort.
/// Shared across test classes via <see cref="ApiCollection"/> so one container serves the whole run.
/// </summary>
public sealed class RushDayApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:18")
        .Build();

    public static SeedOptions SeedOptions { get; } = new() { StudentCount = 300 };

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();

        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RushDayDbContext>();
        await db.Database.MigrateAsync();
        await DatabaseSeeder.SeedAsync(db, SeedOptions);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("ConnectionStrings:RushDay", _postgres.GetConnectionString());
    }

    public new async Task DisposeAsync()
    {
        await base.DisposeAsync();
        await _postgres.DisposeAsync();
    }
}

[CollectionDefinition(Name)]
public sealed class ApiCollection : ICollectionFixture<RushDayApiFactory>
{
    public const string Name = "Api";
}
