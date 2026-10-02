using RushDay.Api.Startup;

var builder = WebApplication.CreateBuilder(args);
builder.AddRushDayServices();

var app = builder.Build();

// Demo guard, KEK check, migration, seed (demo or --migrate-and-seed only) and backfills; false means "exit now".
if (!await StartupTasks.RunAsync(app, args))
{
    return;
}

app.UseRushDayPipeline();
app.MapRushDayEndpoints();

app.Run();

/// <summary>Exposed so integration tests can host the app with WebApplicationFactory.</summary>
public partial class Program;
