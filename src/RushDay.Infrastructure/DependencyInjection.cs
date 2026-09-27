using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RushDay.Infrastructure.Persistence;

namespace RushDay.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddRushDayPersistence(this IServiceCollection services, string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        services.AddDbContext<RushDayDbContext>(options => ConfigureRushDay(options, connectionString));

        return services;
    }

    /// <summary>Single place for provider settings, shared by runtime DI and the design-time factory.</summary>
    internal static void ConfigureRushDay(DbContextOptionsBuilder options, string connectionString) =>
        options.UseNpgsql(connectionString)
               .UseSnakeCaseNamingConvention();
}
