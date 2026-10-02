using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using RushDay.Api.Observability;
using RushDay.Domain.Audit;
using RushDay.Infrastructure.Accounts;
using RushDay.Infrastructure.Identity;
using RushDay.Infrastructure.Persistence;
using RushDay.Infrastructure.Seeding;

namespace RushDay.IntegrationTests.Auth;

/// <summary>
/// The S2 review fixes to sessions and the second factor (02-api.md sections 2.1–2.4, 03 T1, T12, T17): stamp
/// re-validation of active sessions, the bound and non-sliding MFA challenge, TOTP replay, students without a factor,
/// and the audited change-password lockout.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class SessionHardeningTests(RushDayApiFactory factory)
{
    /// <summary>
    /// A session used every two minutes renews its cookie each time; the five-minute stamp check must still run (it
    /// is measured from the last check, never from the renewed cookie's issue time).
    /// </summary>
    [Fact]
    public async Task Active_session_of_a_disabled_user_ends_within_the_interval()
    {
        var clock = new FakeTimeProvider(RushDayApiFactory.ClockStart);
        await using var host = factory.Derive(b => b.UseSetting("Auth:SecurityStampIntervalMinutes", "5"), clock);
        var user = await host.ProvisionAsync();
        using var client = await host.LoginAsync(user.UserName!, TestAccounts.Password);

        await using (var scope = host.Services.CreateAsyncScope())
        {
            Assert.True((await scope.ServiceProvider.GetRequiredService<AccountService>().DisableAsync(user.Id)).Succeeded);
        }

        for (var minute = 2; minute <= 4; minute += 2)
        {
            clock.Advance(TimeSpan.FromMinutes(2));
            using var withinInterval = await client.GetAsync("/api/auth/me");
            Assert.Equal(HttpStatusCode.OK, withinInterval.StatusCode);
        }

        clock.Advance(TimeSpan.FromMinutes(1) + TimeSpan.FromSeconds(1));
        using var ended = await client.GetAsync("/api/auth/me");
        await ended.AssertProblemAsync(HttpStatusCode.Unauthorized, "unauthenticated");
    }

    [Fact]
    public async Task Renewed_cookie_replayed_after_logout_is_rejected_within_the_interval()
    {
        var clock = new FakeTimeProvider(RushDayApiFactory.ClockStart);
        await using var host = factory.Derive(b => b.UseSetting("Auth:SecurityStampIntervalMinutes", "5"), clock);
        var user = await host.ProvisionAsync();
        using var client = host.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false, AllowAutoRedirect = false });

        using var csrf = await client.GetAsync("/api/auth/csrf");
        var csrfCookie = csrf.SetCookies()["rushday.csrf"];
        var anonymousToken = (await csrf.ReadJsonAsync()).GetProperty("csrfToken").GetString()!;
        using var loginRequest = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login") { Content = JsonContent.Create(new { username = user.UserName, password = TestAccounts.Password }) };
        loginRequest.Headers.Add("Cookie", $"rushday.csrf={csrfCookie}");
        loginRequest.Headers.Add(TestClients.CsrfHeader, anonymousToken);
        using var login = await client.SendAsync(loginRequest);
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var authCookie = login.SetCookies()["rushday.auth"];
        var token = (await login.ReadJsonAsync()).GetProperty("csrfToken").GetString()!;

        // Keep the newest cookie, as a browser (or whoever copied it) would.
        for (var minute = 2; minute <= 4; minute += 2)
        {
            clock.Advance(TimeSpan.FromMinutes(2));
            using var renewed = await SendAsync(client, HttpMethod.Get, "/api/auth/me", csrfCookie, authCookie);
            Assert.Equal(HttpStatusCode.OK, renewed.StatusCode);
            if (renewed.SetCookies().TryGetValue("rushday.auth", out var newer))
            {
                authCookie = newer;
            }
        }

        using (var logout = await SendAsync(client, HttpMethod.Post, "/api/auth/logout", csrfCookie, authCookie, token))
        {
            Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);
        }

        clock.Advance(TimeSpan.FromMinutes(1) + TimeSpan.FromSeconds(1));
        using var replay = await SendAsync(client, HttpMethod.Get, "/api/auth/me", csrfCookie, authCookie);
        await replay.AssertProblemAsync(HttpStatusCode.Unauthorized, "unauthenticated");
    }

    [Fact]
    public async Task Mfa_challenge_expires_five_minutes_after_the_password_step()
    {
        var clock = new FakeTimeProvider(RushDayApiFactory.ClockStart);
        await using var host = factory.Derive(clock: clock);
        var (admin, sharedKey) = await AdminWithMfaAsync(host);

        using var client = host.CreateCookieClient();
        await BeginChallengeAsync(client, admin.UserName!);

        // A wrong code three minutes in: a sliding challenge cookie would be renewed for another five minutes here.
        clock.Advance(TimeSpan.FromMinutes(3));
        using (var wrong = await client.PostAsJsonAsync("/api/auth/mfa/verify", new { code = WrongCode(sharedKey) }))
        {
            await wrong.AssertProblemAsync(HttpStatusCode.Unauthorized, "invalid-credentials");
        }

        clock.Advance(TimeSpan.FromMinutes(2) + TimeSpan.FromSeconds(1));
        using var late = await client.PostAsJsonAsync("/api/auth/mfa/verify", new { code = Totp.FreshCode(sharedKey) });
        await late.AssertProblemAsync(HttpStatusCode.Unauthorized, "invalid-credentials");
    }

    [Fact]
    public async Task Password_reset_invalidates_an_outstanding_mfa_challenge()
    {
        var (admin, sharedKey) = await AdminWithMfaAsync(factory);
        using var client = factory.CreateCookieClient();
        await BeginChallengeAsync(client, admin.UserName!);

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            Assert.True((await scope.ServiceProvider.GetRequiredService<AccountService>().ResetPasswordAsync(admin.Id, TestAccounts.OtherPassword)).Succeeded);
        }

        using var verify = await client.PostAsJsonAsync("/api/auth/mfa/verify", new { code = Totp.FreshCode(sharedKey) });
        await verify.AssertProblemAsync(HttpStatusCode.Unauthorized, "invalid-credentials");
        Assert.DoesNotContain(verify.SetCookieHeaders(), c => c.StartsWith("rushday.auth=", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Totp_code_is_refused_on_second_use()
    {
        var admin = await factory.ProvisionAsync();
        using var client = await factory.LoginAsync(admin.UserName!, TestAccounts.Password);
        using var setup = await client.PostAsync("/api/auth/mfa/setup", null);
        var sharedKey = (await setup.ReadJsonAsync()).GetProperty("sharedKey").GetString()!;
        var enableCode = Totp.FreshCode(sharedKey);
        using (var enable = await client.PostAsJsonAsync("/api/auth/mfa/enable", new { code = enableCode }))
        {
            Assert.Equal(HttpStatusCode.OK, enable.StatusCode);
            client.UseCsrf((await enable.ReadJsonAsync()).GetProperty("csrfToken").GetString()!);
        }

        using (var logout = await client.PostAsync("/api/auth/logout", null))
        {
            Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);
        }

        await BeginChallengeAsync(client, admin.UserName!);

        // The code that enabled the factor, seen once, does not sign anybody in.
        using (var replay = await client.PostAsJsonAsync("/api/auth/mfa/verify", new { code = enableCode }))
        {
            await replay.AssertProblemAsync(HttpStatusCode.Unauthorized, "invalid-credentials");
        }

        using var fresh = await client.PostAsJsonAsync("/api/auth/mfa/verify", new { code = Totp.FreshCode(sharedKey) });
        Assert.Equal(HttpStatusCode.OK, fresh.StatusCode);
    }

    /// <summary>Both routes that check a code (verify and enable) go through this provider.</summary>
    [Fact]
    public async Task Authenticator_provider_accepts_each_time_step_once()
    {
        var admin = await factory.ProvisionAsync();
        await using var scope = factory.Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = (await users.FindByIdAsync(admin.Id.ToString()))!;
        await users.ResetAuthenticatorKeyAsync(user);
        var sharedKey = (await users.GetAuthenticatorKeyAsync(user))!;
        var provider = users.Options.Tokens.AuthenticatorTokenProvider;

        var code = Totp.FreshCode(sharedKey);
        Assert.True(await users.VerifyTwoFactorTokenAsync(user, provider, code));
        Assert.False(await users.VerifyTwoFactorTokenAsync(user, provider, code));
        Assert.True(await users.VerifyTwoFactorTokenAsync(user, provider, Totp.FreshCode(sharedKey)));
        Assert.False(await users.VerifyTwoFactorTokenAsync(user, provider, code));
    }

    [Fact]
    public async Task Students_cannot_enrol_a_second_factor()
    {
        using var client = await factory.LoginAsync("S000021", DemoAccounts.StudentPassword);

        using (var setup = await client.PostAsync("/api/auth/mfa/setup", null))
        {
            await setup.AssertProblemAsync(HttpStatusCode.Forbidden, "forbidden");
        }

        using var enable = await client.PostAsJsonAsync("/api/auth/mfa/enable", new { code = "123456" });
        await enable.AssertProblemAsync(HttpStatusCode.Forbidden, "forbidden");
    }

    [Fact]
    public async Task Change_password_lockout_is_audited_and_stops_further_guesses()
    {
        var user = await factory.ProvisionAsync();
        using var client = await factory.LoginAsync(user.UserName!, TestAccounts.Password, "198.51.100.40");
        var metrics = factory.Services.GetRequiredService<MetricsSnapshotService>();
        var lockoutsBefore = metrics.GetSnapshot().Auth.Lockouts;

        for (var attempt = 0; attempt < 5; attempt++)
        {
            using var wrong = await client.PostAsJsonAsync("/api/auth/change-password", new { currentPassword = "Not-My-Password-" + attempt, newPassword = TestAccounts.OtherPassword });
            await wrong.AssertProblemAsync(HttpStatusCode.BadRequest, "invalid-current-password");
        }

        var locked = await factory.ReadUserAsync(user.Id);
        Assert.True(locked.LockoutEnd > DateTimeOffset.UtcNow);

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RushDayDbContext>();
            var audit = await db.AuditEvents.AsNoTracking().SingleAsync(a => a.Action == AuditActions.AuthLockedOut && a.SubjectId == user.Id.ToString());
            using var details = JsonDocument.Parse(audit.Details!);
            Assert.Equal(12, details.RootElement.GetProperty("usernameHash").GetString()!.Length);
            Assert.Equal(5, details.RootElement.GetProperty("failedCount").GetInt32());
            Assert.Equal(32, details.RootElement.GetProperty("ipHash").GetString()!.Length);
            Assert.DoesNotContain(user.UserName!, audit.Details!, StringComparison.OrdinalIgnoreCase);
        }

        Assert.Equal(lockoutsBefore + 1, metrics.GetSnapshot().Auth.Lockouts);

        // The session outlives the lockout until its next stamp check, but may not keep guessing: even the right
        // current password is refused without being checked, and nothing changes.
        using (var right = await client.PostAsJsonAsync("/api/auth/change-password", new { currentPassword = TestAccounts.Password, newPassword = TestAccounts.OtherPassword }))
        {
            await right.AssertProblemAsync(HttpStatusCode.BadRequest, "invalid-current-password");
        }

        await using var check = factory.Services.CreateAsyncScope();
        var users = check.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var stored = (await users.FindByIdAsync(user.Id.ToString()))!;
        Assert.True(await users.CheckPasswordAsync(stored, TestAccounts.Password));
    }

    /// <summary>Provisions an administrator, enrols a factor through the API and signs out; returns the shared key.</summary>
    private static async Task<(ApplicationUser Admin, string SharedKey)> AdminWithMfaAsync(WebApplicationFactory<Program> host)
    {
        var admin = await host.ProvisionAsync();
        using var client = await host.LoginAsync(admin.UserName!, TestAccounts.Password);
        using var setup = await client.PostAsync("/api/auth/mfa/setup", null);
        Assert.Equal(HttpStatusCode.OK, setup.StatusCode);
        var sharedKey = (await setup.ReadJsonAsync()).GetProperty("sharedKey").GetString()!;
        using var enable = await client.PostAsJsonAsync("/api/auth/mfa/enable", new { code = Totp.FreshCode(sharedKey) });
        Assert.Equal(HttpStatusCode.OK, enable.StatusCode);
        client.UseCsrf((await enable.ReadJsonAsync()).GetProperty("csrfToken").GetString()!);
        using var logout = await client.PostAsync("/api/auth/logout", null);
        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);
        return (admin, sharedKey);
    }

    /// <summary>csrf → the password step → <c>{ mfaRequired }</c>, leaving the challenge cookie in the client.</summary>
    private static async Task BeginChallengeAsync(HttpClient client, string username)
    {
        await client.RefreshCsrfAsync();
        using var login = await client.PostLoginAsync(username, TestAccounts.Password);
        var challenge = await login.ReadJsonAsync();
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        Assert.True(challenge.GetProperty("mfaRequired").GetBoolean());
        client.UseCsrf(challenge.GetProperty("csrfToken").GetString()!);
    }

    /// <summary>A six-digit code that none of the five accepted time steps produces.</summary>
    private static string WrongCode(string sharedKey)
    {
        var now = DateTimeOffset.UtcNow;
        var valid = Enumerable.Range(-2, 5).Select(i => Totp.Code(sharedKey, now.AddSeconds(30 * i))).ToHashSet(StringComparer.Ordinal);
        return Enumerable.Range(0, 10).Select(d => new string((char)('0' + d), 6)).First(c => !valid.Contains(c));
    }

    private static async Task<HttpResponseMessage> SendAsync(HttpClient client, HttpMethod method, string path, string csrfCookie, string authCookie, string? csrfToken = null)
    {
        using var request = new HttpRequestMessage(method, path);
        request.Headers.Add("Cookie", $"rushday.csrf={csrfCookie}; rushday.auth={authCookie}");
        if (csrfToken is not null)
        {
            request.Headers.Add(TestClients.CsrfHeader, csrfToken);
        }

        return await client.SendAsync(request);
    }
}
