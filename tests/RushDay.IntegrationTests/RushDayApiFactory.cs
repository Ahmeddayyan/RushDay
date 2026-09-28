using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Time.Testing;
using Npgsql;
using Testcontainers.PostgreSql;

namespace RushDay.IntegrationTests;

/// <summary>
/// Hosts the real API against a throwaway PostgreSQL database (06-implementation-plan.md section 1 rule 8). In CI the
/// database is a Testcontainers instance; locally, where Docker is not available, set <c>RUSHDAY_TEST_CONNECTION</c>
/// (D23) and the factory creates and drops the named database on that native server instead. The host's own
/// <c>StartupTasks</c> migrate it, seed 300 students with the past results day of D24 and run the backfills with demo
/// mode on, all under <see cref="Clock"/>, a fake clock that starts at 2026-09-27T12:00:00Z. Security stamps are
/// validated on every request (interval 0) once the clock has moved. Shared across test classes via
/// <see cref="ApiCollection"/>; <see cref="Derive"/> and <see cref="Production"/> host variants on the same database.
/// </summary>
public sealed class RushDayApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    public const string NativeConnectionVariable = "RUSHDAY_TEST_CONNECTION";

    public const int SeedStudentCount = 300;

    /// <summary>In the past (D24) so seeded autumn results are visible whatever today's date is.</summary>
    public static readonly DateTimeOffset SeedResultsDay = new(2026, 1, 26, 9, 0, 0, TimeSpan.Zero);

    public static readonly DateTimeOffset ClockStart = new(2026, 9, 27, 12, 0, 0, TimeSpan.Zero);

    /// <summary>The fixed key-encryption key of the Production-mode hosts (32 bytes, base64).</summary>
    public static readonly string TestKeyEncryptionKey = Convert.ToBase64String([.. Enumerable.Range(1, 32).Select(i => (byte)(i * 7))]);

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

    /// <summary>The shared fake clock; advance it by seconds at most (other tests assume the same day), never back.</summary>
    public FakeTimeProvider Clock { get; } = new(ClockStart);

    public string ConnectionString => _connectionString ?? throw new InvalidOperationException("The database is not ready; InitializeAsync must run first.");

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

        // Starting the host runs StartupTasks: migrate, seed, backfills.
        _ = Services;
    }

    /// <summary>
    /// Another host on the same database that does no startup work of its own (no migration, seed or backfills), for
    /// tests that need different settings. <paramref name="clock"/> isolates time for tests that move it by hours.
    /// </summary>
    public WebApplicationFactory<Program> Derive(Action<IWebHostBuilder>? configure = null, TimeProvider? clock = null) =>
        WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Database:MigrateOnStartup", "false");
            builder.UseSetting("Database:SeedOnStartup", "false");
            builder.UseSetting("Database:BackfillOnStartup", "false");
            if (clock is not null)
            {
                builder.ConfigureTestServices(services => services.AddSingleton(clock));
            }

            configure?.Invoke(builder);
        });

    /// <summary>A Production-environment host with the fixed test KEK and the public-demo acknowledgement.</summary>
    public WebApplicationFactory<Program> Production(Action<IWebHostBuilder>? configure = null) =>
        Derive(builder =>
        {
            builder.UseEnvironment(Environments.Production);
            builder.UseSetting("DataProtection:KeyEncryptionKey", TestKeyEncryptionKey);
            builder.UseSetting("Demo:PublicDemoAcknowledged", "true");
            configure?.Invoke(builder);
        });

    /// <summary>The configuration of the main host, for hosts composed outside WebApplicationFactory.</summary>
    public IReadOnlyDictionary<string, string?> Settings(bool startupWork) => new Dictionary<string, string?>
    {
        ["ConnectionStrings:RushDay"] = ConnectionString,
        ["Database:MigrateOnStartup"] = startupWork ? "true" : "false",
        ["Database:SeedOnStartup"] = startupWork ? "true" : "false",
        ["Database:BackfillOnStartup"] = startupWork ? "true" : "false",
        ["Database:SeedStudentCount"] = SeedStudentCount.ToString(System.Globalization.CultureInfo.InvariantCulture),
        ["Database:SeedResultsDay"] = SeedResultsDay.ToString("O"),
        ["Database:MaxPoolSize"] = "20",
        ["Demo:Enabled"] = "true",
        ["Auth:SecurityStampIntervalMinutes"] = "0",
        ["Security:TrustForwardedHeaders"] = "true",
    };

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(Environments.Development);
        foreach (var (key, value) in Settings(startupWork: true))
        {
            builder.UseSetting(key, value);
        }

        builder.ConfigureTestServices(services => services.AddSingleton<TimeProvider>(Clock));
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
