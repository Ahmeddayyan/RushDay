using Microsoft.Extensions.DependencyInjection;

namespace RushDay.IntegrationTests.Endpoints;

/// <summary>The factory's fake clock is the application's clock (06-implementation-plan.md section 1 rule 8).</summary>
[Collection(ApiCollection.Name)]
public sealed class FactoryClockTests(RushDayApiFactory factory)
{
    [Fact]
    public async Task Server_time_starts_at_the_fake_clock()
    {
        using var client = factory.CreateCookieClient();

        using var response = await client.GetAsync("/api/public/status");
        var serverTime = (await response.ReadJsonAsync()).GetProperty("serverTime").GetString();

        Assert.StartsWith("2026-09-27", serverTime, StringComparison.Ordinal);
    }

    [Fact]
    public void Fake_clock_is_the_registered_time_provider()
    {
        Assert.Same(factory.Clock, factory.Services.GetRequiredService<TimeProvider>());
        Assert.Equal(RushDayApiFactory.ClockStart.Date, factory.Clock.GetUtcNow().Date);
    }
}
