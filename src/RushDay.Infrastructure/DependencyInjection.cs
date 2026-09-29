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

    public static IServiceCollection AddRushDayPersistence(this IServiceCollection services, string connectionString, int maxPoolSize) =>
        services.AddRushDayPersistence(connectionString, maxPoolSize, allowErrorDetail: false);

    /// <param name="services">The service collection.</param>
    /// <param name="connectionString">The application's connection string.</param>
    /// <param name="maxPoolSize">Npgsql Maximum Pool Size unless the string sets one.</param>
    /// <param name="allowErrorDetail">
    /// True only in Development: an explicit <c>Include Error Detail=true</c> is then honoured; otherwise it is forced
    /// off, because the detail carries row values (marks) into exceptions and logs (T10).
    /// </param>
    public static IServiceCollection AddRushDayPersistence(this IServiceCollection services, string connectionString, int maxPoolSize, bool allowErrorDetail)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxPoolSize, 1);

        var effectiveConnectionString = BuildConnectionString(connectionString, maxPoolSize, allowErrorDetail);
        services.AddDbContext<RushDayDbContext>(options => ConfigureRushDay(options, effectiveConnectionString));

        return services;
    }

    /// <summary>
    /// Applies the pool, timeout and hygiene settings of 04-performance-and-ops.md section 5, each only when the
    /// incoming string does not already set it, so an operator can override any of them in the environment; the one
    /// exception is <c>Include Error Detail</c>, which is forced off unless <paramref name="allowErrorDetail"/>.
    /// No minimum pool and no keepalive by default: two always-open connections pinging every 30 s would keep
    /// Neon's compute from auto-suspending; Development may set <c>Keepalive=30</c> for long k6 runs.
    /// </summary>
    public static string BuildConnectionString(string connectionString, int maxPoolSize, bool allowErrorDetail = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        var builder = new NpgsqlConnectionStringBuilder(connectionString);

        SetIfAbsent(builder, "Maximum Pool Size", b => b.MaxPoolSize = maxPoolSize);
        SetIfAbsent(builder, "Timeout", b => b.Timeout = 5);
        SetIfAbsent(builder, "Command Timeout", b => b.CommandTimeout = 10);
        SetIfAbsent(builder, "Connection Idle Lifetime", b => b.ConnectionIdleLifetime = 60);
        SetIfAbsent(builder, "Connection Pruning Interval", b => b.ConnectionPruningInterval = 10);
        SetIfAbsent(builder, "Application Name", b => b.ApplicationName = "rushday-api");
        if (allowErrorDetail)
        {
            SetIfAbsent(builder, "Include Error Detail", b => b.IncludeErrorDetail = false);
        }
        else
        {
            builder.IncludeErrorDetail = false;
        }

        return builder.ConnectionString;
    }

    /// <summary>Single place for provider settings, shared by runtime DI and the design-time factory.</summary>
    internal static void ConfigureRushDay(DbContextOptionsBuilder options, string connectionString) =>
        options.UseNpgsql(connectionString)
               .UseSnakeCaseNamingConvention();

    private static void SetIfAbsent(NpgsqlConnectionStringBuilder builder, string canonicalKeyword, Action<NpgsqlConnectionStringBuilder> apply)
    {
        // Npgsql stores every alias under its canonical keyword, so ShouldSerialize reports whether the setting was
        // explicitly present in any spelling (ContainsKey is true for every known keyword and cannot be used).
        if (!builder.ShouldSerialize(canonicalKeyword))
        {
            apply(builder);
        }
    }
}
