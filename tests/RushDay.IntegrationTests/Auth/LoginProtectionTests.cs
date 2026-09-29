using System.Data.Common;
using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using RushDay.Infrastructure.Persistence;
using RushDay.Infrastructure.Seeding;

namespace RushDay.IntegrationTests.Auth;

/// <summary>
/// The S2 review fixes to the login protection (02-api.md sections 2.3 and 5, 03 T1, T19): address keys by IPv6 /64,
/// failed-outcomes-only windows without a check-then-consume race, and a CPU guard held only around PBKDF2.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class LoginProtectionTests(RushDayApiFactory factory)
{
    /// <summary>One host holds a whole /64; rotating through it must not look like three addresses.</summary>
    [Fact]
    public async Task Addresses_in_one_ipv6_64_count_as_one_for_lockout()
    {
        var user = await factory.ProvisionAsync();
        string[] sameSubnet = ["2001:db8:1:1::1", "2001:db8:1:1::2", "2001:db8:1:1:ffff:ffff:ffff:3"];
        var clients = sameSubnet.Select(ip => factory.CreateCookieClient(ip)).ToList();
        try
        {
            foreach (var client in clients)
            {
                await client.RefreshCsrfAsync();
            }

            for (var attempt = 0; attempt < 7; attempt++)
            {
                using var failed = await clients[attempt % 3].PostLoginAsync(user.UserName!, "Wrong-Password-" + attempt);
                await failed.AssertProblemAsync(HttpStatusCode.Unauthorized, "invalid-credentials");
            }

            var untouched = await factory.ReadUserAsync(user.Id);
            Assert.Equal(0, untouched.AccessFailedCount);
            Assert.Null(untouched.LockoutEnd);
        }
        finally
        {
            clients.ForEach(c => c.Dispose());
        }
    }

    /// <summary>Only failures count per username: the account's owner signing in repeatedly never spends the window.</summary>
    [Fact]
    public async Task Successful_logins_do_not_spend_the_per_username_window()
    {
        await using var host = factory.Derive(b => b.UseSetting("RateLimiting:LoginPerUserPerMinute", "2"));
        using var client = host.CreateCookieClient();

        for (var attempt = 0; attempt < 4; attempt++)
        {
            await client.RefreshCsrfAsync();
            using var login = await client.PostLoginAsync("S000014", DemoAccounts.StudentPassword);
            Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        }
    }

    /// <summary>
    /// Twenty concurrent failures from one address against a window of five: each attempt reserves its permit before the
    /// password is checked, so exactly five get through to a 401 and the rest are refused.
    /// </summary>
    [Fact]
    public async Task Concurrent_failures_cannot_overrun_the_per_address_window()
    {
        await using var host = factory.Derive(b => b.UseSetting("RateLimiting:LoginFailuresPerIpPer10Minutes", "5"));
        using var client = host.CreateCookieClient("203.0.113.30");
        await client.RefreshCsrfAsync();

        var responses = await Task.WhenAll(Enumerable.Range(0, 20).Select(i => client.PostLoginAsync(TestAccounts.NewUsername("nobody"), "Wrong-Password-" + i)));
        try
        {
            Assert.Equal(5, responses.Count(r => r.StatusCode == HttpStatusCode.Unauthorized));
            Assert.Equal(15, responses.Count(r => r.StatusCode == HttpStatusCode.TooManyRequests));
        }
        finally
        {
            foreach (var response in responses)
            {
                response.Dispose();
            }
        }
    }

    /// <summary>
    /// With one CPU permit and no queue, a second sign-in must still get the permit while the first is completing its
    /// sign-in (a database round trip, held here): the guard covers PBKDF2, not the work after it.
    /// </summary>
    [Fact]
    public async Task Cpu_guard_is_released_before_the_sign_in_completes()
    {
        var gate = new SignInCompletionGate();
        await using var host = factory.Derive(b =>
        {
            b.UseSetting("RateLimiting:LoginConcurrency", "1");
            b.UseSetting("RateLimiting:LoginQueue", "0");
            b.ConfigureTestServices(s => s.ConfigureDbContext<RushDayDbContext>((_, o) => o.AddInterceptors(gate)));
        });
        using var first = host.CreateCookieClient("198.51.100.50");
        using var second = host.CreateCookieClient("198.51.100.51");
        await first.RefreshCsrfAsync();
        await second.RefreshCsrfAsync();

        var firstLogin = first.PostLoginAsync("S000015", DemoAccounts.StudentPassword);
        try
        {
            await gate.Entered.Task.WaitAsync(TimeSpan.FromSeconds(30));
            var secondLogin = second.PostLoginAsync("S000016", DemoAccounts.StudentPassword);
            var finished = await Task.WhenAny(secondLogin, Task.Delay(TimeSpan.FromSeconds(5)));
            if (finished == secondLogin)
            {
                // It may only finish early by failing; a 429 means the first sign-in still held the CPU permit.
                using var early = await secondLogin;
                Assert.NotEqual(HttpStatusCode.TooManyRequests, early.StatusCode);
            }

            gate.Release.TrySetResult();
            using var secondResponse = await secondLogin;
            Assert.Equal(HttpStatusCode.OK, secondResponse.StatusCode);
        }
        finally
        {
            gate.Release.TrySetResult();
        }

        using var firstResponse = await firstLogin;
        Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);
    }

    /// <summary>Holds the <c>last_login_at</c> update of the sign-in completion until released.</summary>
    private sealed class SignInCompletionGate : DbCommandInterceptor
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("last_login_at", StringComparison.Ordinal))
            {
                Entered.TrySetResult();
                await Release.Task.WaitAsync(TimeSpan.FromSeconds(30), cancellationToken);
            }

            return result;
        }
    }
}
