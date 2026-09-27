using Microsoft.EntityFrameworkCore;
using RushDay.Api;
using RushDay.Api.Endpoints;
using RushDay.Infrastructure;
using RushDay.Infrastructure.Persistence;
using RushDay.Infrastructure.Seeding;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("RushDay")
    ?? throw new InvalidOperationException("Connection string 'RushDay' is not configured.");
var databaseOptions = builder.Configuration.GetSection(DatabaseOptions.SectionName).Get<DatabaseOptions>()
    ?? new DatabaseOptions();

builder.Services.AddOpenApi();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddRushDayPersistence(connectionString);
builder.Services.AddHealthChecks().AddDbContextCheck<RushDayDbContext>();

var app = builder.Build();

var seedCommand = args.Contains("--migrate-and-seed");
if (seedCommand || databaseOptions.MigrateOnStartup)
{
    await using var scope = app.Services.CreateAsyncScope();
    var db = scope.ServiceProvider.GetRequiredService<RushDayDbContext>();
    await db.Database.MigrateAsync();
    app.Logger.LogInformation("Database migrated.");

    if (seedCommand || databaseOptions.SeedOnStartup)
    {
        await DatabaseSeeder.SeedAsync(db, new SeedOptions { StudentCount = databaseOptions.SeedStudentCount });
        app.Logger.LogInformation("Database seeded.");
    }
}

if (seedCommand)
{
    return;
}

app.MapOpenApi();
app.MapHealthChecks("/health");
app.MapGet("/", () => TypedResults.Ok(new
{
    name = "RushDay",
    story = "A university student portal built to fall over on results day, then fixed one measured step at a time.",
    commit = Environment.GetEnvironmentVariable("RENDER_GIT_COMMIT") ?? "local",
    links = new
    {
        health = "/health",
        openapi = "/openapi/v1.json",
        dashboard = "/students/S000001/dashboard",
        modules = "/modules",
        hotModule = $"/modules/{DatabaseSeeder.HotModuleCode}",
    },
})).ExcludeFromDescription();

app.MapStudentEndpoints();
app.MapModuleEndpoints();

app.Run();

/// <summary>Exposed so integration tests can host the app with WebApplicationFactory.</summary>
public partial class Program;
