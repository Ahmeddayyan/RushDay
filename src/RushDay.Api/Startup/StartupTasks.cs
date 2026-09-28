using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using RushDay.Api.Options;
using RushDay.Api.Security;
using RushDay.Infrastructure;
using RushDay.Infrastructure.Persistence;
using RushDay.Infrastructure.Seeding;

namespace RushDay.Api.Startup;

/// <summary>
/// Everything that happens between <c>Build()</c> and <c>Run()</c> (04-performance-and-ops.md section 7, 01 section 6):
/// the demo guard → the KEK check → migrate (on <c>ConnectionStrings:Migrations</c> when set) → seed, only in demo mode
/// or under <c>--migrate-and-seed</c> → the idempotent backfills, each step logged with its elapsed time. Any failure
/// aborts startup, so Render keeps the previous instance (its deploy health check never passes).
/// </summary>
public static class StartupTasks
{
    public const string MigrateAndSeedArgument = "--migrate-and-seed";

    public const string DemoWithoutAcknowledgementMessage = "Demo mode on a Production deployment requires Demo__PublicDemoAcknowledged=true";

    public const string DemoModeWarning = "DEMO MODE: every account has a published password";

    /// <summary>Runs the startup steps; returns false when the process should exit (under <c>--migrate-and-seed</c>).</summary>
    public static async Task<bool> RunAsync(WebApplication app, string[] args)
    {
        ArgumentNullException.ThrowIfNull(app);
        ArgumentNullException.ThrowIfNull(args);

        var services = app.Services;
        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger("RushDay.Startup");
        var environment = app.Environment;
        var configuration = app.Configuration;
        var demo = services.GetRequiredService<IOptions<DemoOptions>>().Value;
        var database = services.GetRequiredService<IOptions<DatabaseOptions>>().Value;
        var seedCommand = args.Contains(MigrateAndSeedArgument, StringComparer.Ordinal);

        // 1. Demo guard (D29).
        if (environment.IsProduction() && demo.Enabled)
        {
            if (!demo.PublicDemoAcknowledged)
            {
                throw new InvalidOperationException(DemoWithoutAcknowledgementMessage);
            }

            logger.LogWarning(DemoModeWarning);
        }

        // 2. KEK check (D31): outside Development the key ring must be encrypted.
        var kek = DataProtectionKeyEncryptionKey.FromConfiguration(services.GetRequiredService<IOptions<DataProtectionOptions>>().Value.KeyEncryptionKey);
        if (!environment.IsDevelopment() && kek is null)
        {
            throw new InvalidOperationException(DataProtectionKeyEncryptionKey.MissingMessage);
        }

        var hosts = HostFilteringSetup.ResolveAllowedHosts(configuration, environment, services.GetRequiredService<IOptions<SecurityOptions>>().Value);
        if (!environment.IsDevelopment() && HostFilteringSetup.IsDisabled(hosts))
        {
            logger.LogWarning(HostFilteringSetup.DisabledWarning);
        }

        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<RushDayDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<TimeProvider>();

        // 3. Migrate.
        if (seedCommand || database.MigrateOnStartup)
        {
            var stopwatch = Stopwatch.StartNew();
            var migrationsConnection = configuration.GetConnectionString("Migrations");
            if (!string.IsNullOrWhiteSpace(migrationsConnection))
            {
                var options = new DbContextOptionsBuilder<RushDayDbContext>()
                    .UseNpgsql(migrationsConnection)
                    .UseSnakeCaseNamingConvention()
                    .Options;
                await using var migrations = new RushDayDbContext(options);
                await migrations.Database.MigrateAsync();
            }
            else
            {
                await db.Database.MigrateAsync();
            }

            logger.LogInformation(
                "Database migrated on the {Connection} connection in {ElapsedMs} ms.",
                string.IsNullOrWhiteSpace(migrationsConnection) ? "application" : "migrations",
                stopwatch.ElapsedMilliseconds);
        }

        // 4. Seed: only for the demo (D29); a customer database starts with nothing synthetic.
        if (seedCommand || (database.SeedOnStartup && demo.Enabled))
        {
            var stopwatch = Stopwatch.StartNew();
            await DatabaseSeeder.SeedAsync(db, new SeedOptions
            {
                StudentCount = database.SeedStudentCount,
                ResultsDay = database.SeedResultsDay,
            });
            logger.LogInformation("Database seeded in {ElapsedMs} ms.", stopwatch.ElapsedMilliseconds);
        }

        // 5. Backfills.
        if (seedCommand || database.BackfillOnStartup)
        {
            var stopwatch = Stopwatch.StartNew();
            var bootstrap = services.GetRequiredService<IOptions<BootstrapOptions>>().Value;
            var branding = services.GetRequiredService<IOptions<BrandingOptions>>().Value;
            var backfillOptions = new StartupBackfillOptions
            {
                DemoEnabled = demo.Enabled,
                BootstrapAdminUsername = string.IsNullOrWhiteSpace(bootstrap.AdminUsername) ? "admin" : bootstrap.AdminUsername,
                BootstrapAdminPassword = bootstrap.AdminPassword,
                InstitutionName = branding.InstitutionName,
                InstitutionShortName = branding.InstitutionShortName,
                TimeZone = branding.TimeZone,
                SeedResultsDay = database.SeedResultsDay,
            };
            await StartupBackfills.RunAsync(db, backfillOptions, clock, logger);
            logger.LogInformation("Startup backfills complete in {ElapsedMs} ms.", stopwatch.ElapsedMilliseconds);
        }

        return !seedCommand;
    }

    /// <summary>The pool size the application's connection string is built with (D14).</summary>
    public static int MaxPoolSize(DatabaseOptions database) =>
        database.MaxPoolSize > 0 ? database.MaxPoolSize : DependencyInjection.DefaultMaxPoolSize;
}
