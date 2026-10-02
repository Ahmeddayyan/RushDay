using System.Net;
using RushDay.Infrastructure.Seeding;

namespace RushDay.IntegrationTests.Auth;

/// <summary>
/// A demo session's password is public (the login page shows it), so it may change no real account, and whatever it
/// provisions is demo data that dies with the demo (02-api.md section 8.5, T16). Observed through a real demo
/// administrator session on the probe app's copies of S6's account routes.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class DemoActorTests(RushDayApiFactory factory)
{
    [Fact]
    public async Task Demo_administrator_cannot_lock_a_real_account()
    {
        var real = await factory.ProvisionAsync();
        await using var probe = await ProbeApp.StartAsync(factory);
        using var demoAdmin = await probe.LoginAsync(DemoAccounts.AdminUsername, DemoAccounts.AdminPassword);

        using var response = await demoAdmin.PostAsync($"/api/probe/accounts/{real.Id}/lock", null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("DemoAccount", (await response.ReadJsonAsync()).GetProperty("error").GetString());
        var unchanged = await factory.ReadUserAsync(real.Id);
        Assert.Null(unchanged.LockoutEnd);
        Assert.Equal(real.SecurityStamp, unchanged.SecurityStamp);
    }

    [Fact]
    public async Task Accounts_a_demo_administrator_provisions_are_demo_accounts()
    {
        await using var probe = await ProbeApp.StartAsync(factory);
        using var demoAdmin = await probe.LoginAsync(DemoAccounts.AdminUsername, DemoAccounts.AdminPassword);

        using var response = await demoAdmin.PostAsync("/api/probe/accounts", null);
        var body = await response.ReadJsonAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(body.GetProperty("isDemo").GetBoolean());
        var stored = await factory.ReadUserAsync(body.GetProperty("id").GetGuid());
        Assert.True(stored.IsDemo);
        Assert.False(stored.MustChangePassword);
    }

    [Fact]
    public async Task A_real_actor_provisions_real_accounts()
    {
        // The rule keys on the actor: provisioning by anyone else still makes a real account that must change its password.
        var user = await factory.ProvisionAsync(mustChangePassword: true);

        var stored = await factory.ReadUserAsync(user.Id);
        Assert.False(stored.IsDemo);
        Assert.True(stored.MustChangePassword);
    }
}
