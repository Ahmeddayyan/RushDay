using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using RushDay.Api.Options;
using RushDay.Api.Startup;

namespace RushDay.UnitTests.Startup;

/// <summary>The Production defaults of 03-security.md section 8, as the options system resolves them.</summary>
public sealed class OptionsDefaultsTests
{
    /// <summary>The CPU guard's queue stays below the global limiter's 24 permits (02-api.md section 5).</summary>
    [Fact]
    public void Rate_limiting_defaults_are_the_production_values()
    {
        var limits = new RateLimitingOptions();

        Assert.Equal((24, 96), (limits.MaxConcurrent, limits.MaxQueued));
        Assert.Equal((8, 16), (limits.LoginConcurrency, limits.LoginQueue));
        Assert.True(limits.LoginConcurrency + limits.LoginQueue < limits.MaxConcurrent + limits.MaxQueued);
        Assert.Equal((10, 20, 3), (limits.LoginPerUserPerMinute, limits.LoginFailuresPerIpPer10Minutes, limits.LockoutDistinctIps));
    }

    [Theory]
    [InlineData("Production", true)]
    [InlineData("Staging", true)]
    [InlineData("Development", false)]
    public void Migration_and_backfills_run_on_boot_outside_development(string environment, bool expected)
    {
        var database = Resolve(environment);

        Assert.Equal(expected, database.MigrateOnStartup);
        Assert.Equal(expected, database.BackfillOnStartup);
        Assert.False(database.SeedOnStartup);
        Assert.Equal(TimeSpan.FromSeconds(600), StartupTasks.StartupCommandTimeout(database));
    }

    [Fact]
    public void An_explicit_setting_still_wins_in_production()
    {
        var database = Resolve("Production", ("Database:MigrateOnStartup", "false"), ("Database:StartupCommandTimeoutSeconds", "900"));

        Assert.False(database.MigrateOnStartup);
        Assert.True(database.BackfillOnStartup);
        Assert.Equal(TimeSpan.FromSeconds(900), StartupTasks.StartupCommandTimeout(database));
    }

    private static DatabaseOptions Resolve(string environment, params (string Key, string Value)[] settings)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = environment });
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:RushDay"] = "Host=127.0.0.1;Port=1;Database=never;Username=nobody;Password=nobody",
        });
        builder.Configuration.AddInMemoryCollection(settings.ToDictionary(s => s.Key, s => (string?)s.Value));
        builder.AddRushDayServices();

        using var services = builder.Services.BuildServiceProvider();
        return services.GetRequiredService<IOptions<DatabaseOptions>>().Value;
    }
}
