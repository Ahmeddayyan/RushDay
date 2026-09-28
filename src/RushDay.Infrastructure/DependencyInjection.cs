using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using RushDay.Infrastructure.Persistence;

namespace RushDay.Infrastructure;

public static class DependencyInjection
{
    /// <summary>Production default (04-performance-and-ops.md section 5): Neon's free compute grants about 112 connections.</summary>
    public const int DefaultMaxPoolSize = 20;

    public static IServiceCollection AddRushDayPersistence(this IServiceCollection services, string connectionString) =>
        services.AddRushDayPersistence(connectionString, DefaultMaxPoolSize);

    public static IServiceCollection AddRushDayPersistence(this IServiceCollection services, string connectionString, int maxPoolSize)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxPoolSize, 1);

        var effectiveConnectionString = BuildConnectionString(connectionString, maxPoolSize);
        services.AddDbContext<RushDayDbContext>(options => ConfigureRushDay(options, effectiveConnectionString));

        return services;
    }

    /// <summary>
    /// Applies the pool, timeout and hygiene settings of 04-performance-and-ops.md section 5, each only when the
    /// incoming string does not already set it, so an operator can override any of them in the environment.
    /// </summary>
    public static string BuildConnectionString(string connectionString, int maxPoolSize)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        var builder = new NpgsqlConnectionStringBuilder(connectionString);

        SetIfAbsent(builder, "Maximum Pool Size", b => b.MaxPoolSize = maxPoolSize);
        SetIfAbsent(builder, "Minimum Pool Size", b => b.MinPoolSize = 2);
        SetIfAbsent(builder, "Timeout", b => b.Timeout = 5);
        SetIfAbsent(builder, "Command Timeout", b => b.CommandTimeout = 10);
        SetIfAbsent(builder, "Connection Idle Lifetime", b => b.ConnectionIdleLifetime = 60);
        SetIfAbsent(builder, "Connection Pruning Interval", b => b.ConnectionPruningInterval = 10);
        SetIfAbsent(builder, "Keepalive", b => b.KeepAlive = 30);
        SetIfAbsent(builder, "Application Name", b => b.ApplicationName = "rushday-api");
        SetIfAbsent(builder, "Include Error Detail", b => b.IncludeErrorDetail = false);

        return builder.ConnectionString;
    }

    /// <summary>Single place for provider settings, shared by runtime DI and the design-time factory.</summary>
    internal static void ConfigureRushDay(DbContextOptionsBuilder options, string connectionString) =>
        options.UseNpgsql(connectionString)
               .UseSnakeCaseNamingConvention();

    private static void SetIfAbsent(NpgsqlConnectionStringBuilder builder, string canonicalKeyword, Action<NpgsqlConnectionStringBuilder> apply)
    {
        // ShouldSerialize reports whether the keyword was explicitly present (ContainsKey is true for every known keyword).
        if (!builder.ShouldSerialize(canonicalKeyword))
        {
            apply(builder);
        }
    }
}
