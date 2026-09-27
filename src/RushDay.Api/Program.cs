using Microsoft.EntityFrameworkCore;
using Microsoft.Net.Http.Headers;
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

// wwwroot holds the Vite build of src/RushDay.Web. Hashed files under /assets never change, so they can be
// cached for a year; index.html (and anything else) must be revalidated so a deploy shows up on the next load.
var staticFiles = new StaticFileOptions
{
    OnPrepareResponse = context =>
    {
        var headers = context.Context.Response.GetTypedHeaders();
        headers.CacheControl = context.Context.Request.Path.StartsWithSegments("/assets")
            ? new CacheControlHeaderValue
            {
                Public = true,
                MaxAge = TimeSpan.FromDays(365),
                Extensions = { new NameValueHeaderValue("immutable") },
            }
            : new CacheControlHeaderValue { NoCache = true };
    },
};
app.UseStaticFiles(staticFiles);

app.MapOpenApi();
app.MapHealthChecks("/health");
app.MapGet("/api", () => TypedResults.Ok(new
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

// An unknown /api route is a client bug, not a page: answer with a JSON problem, never with index.html.
app.MapFallback("/api/{**slug}", (HttpContext http) => Results.Problem(
    statusCode: StatusCodes.Status404NotFound,
    title: "Not found",
    detail: $"No API endpoint matches {http.Request.Method} {http.Request.Path}."))
    .ExcludeFromDescription();

// Every other unmatched path without a file extension is a client-side route (/login, /modules, ...):
// hand back index.html and let the router take over.
app.MapFallbackToFile("index.html", staticFiles).ExcludeFromDescription();

app.Run();

/// <summary>Exposed so integration tests can host the app with WebApplicationFactory.</summary>
public partial class Program;
