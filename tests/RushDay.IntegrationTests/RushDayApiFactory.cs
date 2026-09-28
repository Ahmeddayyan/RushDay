using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using RushDay.Infrastructure.Persistence;
using RushDay.Infrastructure.Seeding;
using Testcontainers.PostgreSql;

namespace RushDay.IntegrationTests;

/// <summary>
/// Hosts the real API against a throwaway PostgreSQL database, migrated, seeded with a small cohort and
/// backfilled with demo mode on. In CI the database is a Testcontainers instance; locally, where Docker is not
/// available, set <c>RUSHDAY_TEST_CONNECTION</c> (00-overview.md D23) and the factory creates and drops the named
/// database on that native server instead. Shared across test classes via <see cref="ApiCollection"/>.
/// </summary>
public sealed class RushDayApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    public const string NativeConnectionVariable = "RUSHDAY_TEST_CONNECTION";

    /// <summary>In the past (00-overview.md D24) so seeded autumn results are visible whatever today's date is.</summary>
    public static readonly DateTimeOffset SeedResultsDay = new(2026, 1, 26, 9, 0, 0, TimeSpan.Zero);

    public static SeedOptions SeedOptions { get; } = new() { StudentCount = 300, ResultsDay = SeedResultsDay };

    public static StartupBackfillOptions BackfillOptions { get; } = new() { DemoEnabled = true, SeedResultsDay = SeedResultsDay };

    private readonly string? _nativeConnectionString = Environment.GetEnvironmentVariable(NativeConnectionVariable) is { Length: > 0 } value ? value : null;
    private readonly PostgreSqlContainer? _postgres;
    private string? _connectionString;

    public RushDayApiFactory()
    {
        if (_nativeConnectionString is null)
        {
            _postgres = new PostgreSqlBuilder("postgres:18").Build();
        }
    }

    public async Task InitializeAsync()
    {
        if (_nativeConnectionString is not null)
        {
            await RecreateNativeDatabaseAsync(_nativeConnectionString);
            _connectionString = _nativeConnectionString;
        }
        else
        {
            await _postgres!.StartAsync();
            _connectionString = _postgres.GetConnectionString();
        }

        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RushDayDbContext>();
        await db.Database.MigrateAsync();
        await DatabaseSeeder.SeedAsync(db, SeedOptions);
        await StartupBackfills.RunAsync(db, BackfillOptions, TimeProvider.System, NullLogger.Instance);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("ConnectionStrings:RushDay", _connectionString
            ?? throw new InvalidOperationException("The database is not ready; InitializeAsync must run first."));
        builder.UseSetting("Database:SeedResultsDay", SeedResultsDay.ToString("O"));
    }

    public new async Task DisposeAsync()
    {
        await base.DisposeAsync();

        if (_nativeConnectionString is not null)
        {
            await DropNativeDatabaseAsync(_nativeConnectionString);
        }
        else if (_postgres is not null)
        {
            await _postgres.DisposeAsync();
        }
    }

    private static async Task RecreateNativeDatabaseAsync(string connectionString)
    {
        var (maintenance, database) = MaintenanceConnection(connectionString);

        await using var connection = new NpgsqlConnection(maintenance);
        await connection.OpenAsync();
        await using (var drop = new NpgsqlCommand($"DROP DATABASE IF EXISTS {Quote(database)} WITH (FORCE)", connection))
        {
            await drop.ExecuteNonQueryAsync();
        }

        await using var create = new NpgsqlCommand($"CREATE DATABASE {Quote(database)}", connection);
        await create.ExecuteNonQueryAsync();
    }

    private static async Task DropNativeDatabaseAsync(string connectionString)
    {
        // The host's pool still holds sockets to the test database; close them so DROP ... WITH (FORCE) has nothing to kill.
        NpgsqlConnection.ClearAllPools();

        var (maintenance, database) = MaintenanceConnection(connectionString);
        await using var connection = new NpgsqlConnection(maintenance);
        await connection.OpenAsync();
        await using var drop = new NpgsqlCommand($"DROP DATABASE IF EXISTS {Quote(database)} WITH (FORCE)", connection);
        await drop.ExecuteNonQueryAsync();
    }

    private static (string Maintenance, string Database) MaintenanceConnection(string connectionString)
    {
        var builder = new NpgsqlConnectionStringBuilder(connectionString);
        var database = builder.Database
            ?? throw new InvalidOperationException($"{NativeConnectionVariable} must name a Database.");
        if (string.Equals(database, "postgres", StringComparison.OrdinalIgnoreCase) || string.Equals(database, "rushday", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"{NativeConnectionVariable} must name a throwaway database, not '{database}'.");
        }

        builder.Database = "postgres";
        builder.Pooling = false;
        return (builder.ConnectionString, database);
    }

    private static string Quote(string identifier) => "\"" + identifier.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
}

[CollectionDefinition(Name)]
public sealed class ApiCollection : ICollectionFixture<RushDayApiFactory>
{
    public const string Name = "Api";
}
