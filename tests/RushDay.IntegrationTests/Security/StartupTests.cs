using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Hosting;
using RushDay.Api.Security;
using RushDay.Api.Startup;

namespace RushDay.IntegrationTests.Security;

/// <summary>A Production start refuses an unacknowledged demo or a missing KEK before touching the database (D29, D31, T16, T18).</summary>
[Collection(ApiCollection.Name)]
public sealed class StartupTests
{
    /// <summary>Never contacted: both guards run before the migration.</summary>
    private const string UnreachableDatabase = "Host=127.0.0.1;Port=1;Database=rushday_never;Username=nobody;Password=nobody;Timeout=1";

    [Fact]
    public async Task Production_demo_without_acknowledgement_aborts()
    {
        await using var app = ProductionApp(("Demo:Enabled", "true"), ("DataProtection:KeyEncryptionKey", RushDayApiFactory.TestKeyEncryptionKey));

        var error = Assert.ThrowsAny<Exception>(() => app.CreateClient());

        Assert.Contains(StartupTasks.DemoWithoutAcknowledgementMessage, Messages(error), StringComparison.Ordinal);
    }

    /// <summary>Every environment but Development needs the acknowledgement: a Staging slot is as public as Production.</summary>
    [Fact]
    public async Task Staging_demo_without_acknowledgement_aborts()
    {
        await using var app = App(Environments.Staging, ("Demo:Enabled", "true"), ("DataProtection:KeyEncryptionKey", RushDayApiFactory.TestKeyEncryptionKey));

        var error = Assert.ThrowsAny<Exception>(() => app.CreateClient());

        Assert.Contains(StartupTasks.DemoWithoutAcknowledgementMessage, Messages(error), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Production_without_kek_aborts()
    {
        await using var app = ProductionApp(("Demo:Enabled", "false"));

        var error = Assert.ThrowsAny<Exception>(() => app.CreateClient());

        Assert.Contains(DataProtectionKeyEncryptionKey.MissingMessage, Messages(error), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Production_with_a_malformed_kek_aborts()
    {
        await using var app = ProductionApp(("DataProtection:KeyEncryptionKey", Convert.ToBase64String(new byte[16])));

        var error = Assert.ThrowsAny<Exception>(() => app.CreateClient());

        Assert.Contains("32 bytes", Messages(error), StringComparison.Ordinal);
    }

    private static WebApplicationFactory<Program> ProductionApp(params (string Key, string Value)[] settings) =>
        App(Environments.Production, settings);

    private static WebApplicationFactory<Program> App(string environment, params (string Key, string Value)[] settings) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment(environment);
            builder.UseSetting("ConnectionStrings:RushDay", UnreachableDatabase);
            builder.UseSetting("Database:MigrateOnStartup", "true");
            foreach (var (key, value) in settings)
            {
                builder.UseSetting(key, value);
            }
        });

    private static string Messages(Exception error)
    {
        var messages = new List<string>();
        for (Exception? current = error; current is not null; current = current.InnerException)
        {
            messages.Add(current.Message);
            if (current is AggregateException aggregate)
            {
                messages.AddRange(aggregate.InnerExceptions.Select(e => e.Message));
            }
        }

        return string.Join(" | ", messages);
    }
}
