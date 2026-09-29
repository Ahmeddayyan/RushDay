using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using RushDay.Domain.Audit;
using RushDay.Infrastructure.Accounts;
using RushDay.Infrastructure.Persistence;
using RushDay.Infrastructure.Seeding;

namespace RushDay.IntegrationTests.Auth;

/// <summary>Sessions, lockout, the gates and the second factor (02-api.md sections 2.1–2.4, 03 T1, T12, T16, T17, T19).</summary>
[Collection(ApiCollection.Name)]
public sealed class AuthTests(RushDayApiFactory factory)
{
    private static readonly string[] ThreeAddresses = ["198.51.100.1", "198.51.100.2", "198.51.100.3"];

    [Fact]
    public async Task Login_round_trip_with_cookies_and_antiforgery()
    {
        var user = await factory.ProvisionAsync();
        using var client = factory.CreateCookieClient();

        var anonymousToken = await client.RefreshCsrfAsync();
        using var login = await client.PostLoginAsync(user.UserName!, TestAccounts.Password);
        var me = await login.ReadJsonAsync();
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        Assert.Contains(login.SetCookieHeaders(), c => c.StartsWith("rushday.auth=", StringComparison.Ordinal));
        Assert.Equal(user.UserName, me.GetProperty("username").GetString());
        Assert.Equal("Admin", me.GetProperty("role").GetString());
        var signedInToken = me.GetProperty("csrfToken").GetString()!;
        Assert.NotEqual(anonymousToken, signedInToken);
        client.UseCsrf(signedInToken);

        using (var current = await client.GetAsync("/api/auth/me"))
        {
            Assert.Equal(HttpStatusCode.OK, current.StatusCode);
            Assert.Equal(user.Id.ToString(), (await current.ReadJsonAsync()).GetProperty("id").GetString());
        }

        using (var change = await client.PostAsJsonAsync("/api/auth/change-password", new { currentPassword = TestAccounts.Password, newPassword = TestAccounts.OtherPassword }))
        {
            Assert.Equal(HttpStatusCode.NoContent, change.StatusCode);
        }

        // The current session survives its own password change.
        using (var afterChange = await client.GetAsync("/api/auth/me"))
        {
            Assert.Equal(HttpStatusCode.OK, afterChange.StatusCode);
        }

        using (var logout = await client.PostAsync("/api/auth/logout", null))
        {
            Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);
        }

        using (var afterLogout = await client.GetAsync("/api/auth/me"))
        {
            await afterLogout.AssertProblemAsync(HttpStatusCode.Unauthorized, "unauthenticated");
        }

        using var fresh = factory.CreateCookieClient();
        var again = await fresh.LoginAsync(user.UserName!, TestAccounts.OtherPassword);
        Assert.Equal(user.UserName, again.GetProperty("username").GetString());
    }

    [Fact]
    public async Task Demo_student_signs_in_with_claims_from_the_record()
    {
        using var client = factory.CreateCookieClient();

        var me = await client.LoginAsync("s000001", DemoAccounts.StudentPassword);

        Assert.Equal("S000001", me.GetProperty("username").GetString());
        Assert.Equal("Student", me.GetProperty("role").GetString());
        Assert.Equal("S000001", me.GetProperty("studentNumber").GetString());
        Assert.Equal(JsonValueKind.Null, me.GetProperty("staffNumber").ValueKind);
        Assert.True(me.GetProperty("isDemo").GetBoolean());
        Assert.False(me.GetProperty("mustChangePassword").GetBoolean());
        Assert.False(me.GetProperty("mfaSetupRequired").GetBoolean());
        Assert.False(string.IsNullOrEmpty(me.GetProperty("displayName").GetString()));
    }

    [Fact]
    public async Task Lockout_after_five_failures_from_three_addresses()
    {
        var user = await factory.ProvisionAsync();
        var clients = ThreeAddresses.Select(ip => factory.CreateCookieClient(ip)).ToList();
        try
        {
            foreach (var client in clients)
            {
                await client.RefreshCsrfAsync();
            }

            // Failures 1 and 2 come from one and two addresses: not counted. From the third address on, each counts.
            for (var attempt = 0; attempt < 6; attempt++)
            {
                using var failed = await clients[attempt % 3].PostLoginAsync(user.UserName!, "Wrong-Password-" + attempt);
                await failed.AssertProblemAsync(HttpStatusCode.Unauthorized, "invalid-credentials");
            }

            var beforeLock = await factory.ReadUserAsync(user.Id);
            Assert.Equal(4, beforeLock.AccessFailedCount);
            Assert.Null(beforeLock.LockoutEnd);

            using (var fifth = await clients[0].PostLoginAsync(user.UserName!, "Wrong-Password-final"))
            {
                await fifth.AssertProblemAsync(HttpStatusCode.Unauthorized, "invalid-credentials");
            }

            // Identity sets the lockout end from the real clock, not the factory's fake one: 15 minutes from now.
            var locked = await factory.ReadUserAsync(user.Id);
            var realNow = DateTimeOffset.UtcNow;
            Assert.NotNull(locked.LockoutEnd);
            Assert.InRange(locked.LockoutEnd.Value, realNow.AddMinutes(14), realNow.AddMinutes(16));

            // Even the right password from a fresh address is refused, with the same answer.
            using var correct = factory.CreateCookieClient("198.51.100.4");
            await correct.RefreshCsrfAsync();
            using var refused = await correct.PostLoginAsync(user.UserName!, TestAccounts.Password);
            await refused.AssertProblemAsync(HttpStatusCode.Unauthorized, "invalid-credentials");

            await using var scope = factory.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<RushDayDbContext>();
            var audit = await db.AuditEvents.AsNoTracking().SingleAsync(a => a.Action == AuditActions.AuthLockedOut && a.SubjectId == user.Id.ToString());
            Assert.Equal(AuditSubjects.Account, audit.SubjectType);
            Assert.NotNull(audit.IpHash);
            using var details = JsonDocument.Parse(audit.Details!);
            Assert.Equal(12, details.RootElement.GetProperty("usernameHash").GetString()!.Length);
            Assert.Equal(32, details.RootElement.GetProperty("ipHash").GetString()!.Length);
            Assert.Equal(5, details.RootElement.GetProperty("failedCount").GetInt32());
            Assert.DoesNotContain(user.UserName!, audit.Details!, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            clients.ForEach(c => c.Dispose());
        }
    }

    [Fact]
    public async Task Single_address_spray_is_rate_limited_not_locked()
    {
        await using var host = factory.Derive(b => b.UseSetting("RateLimiting:LoginFailuresPerIpPer10Minutes", "20"));
        var victim = await host.ProvisionAsync();
        using var attacker = host.CreateCookieClient("203.0.113.10");
        await attacker.RefreshCsrfAsync();

        for (var attempt = 0; attempt < 20; attempt++)
        {
            using var failed = await attacker.PostLoginAsync(victim.UserName!, "Spray-Guess-" + attempt);
            await failed.AssertProblemAsync(HttpStatusCode.Unauthorized, "invalid-credentials");
        }

        using (var limited = await attacker.PostLoginAsync(victim.UserName!, "Spray-Guess-final"))
        {
            await limited.AssertProblemAsync(HttpStatusCode.TooManyRequests, "rate-limited");
            Assert.True(limited.Headers.RetryAfter?.Delta?.TotalSeconds >= 1);
        }

        var untouched = await host.ReadUserAsync(victim.Id);
        Assert.Equal(0, untouched.AccessFailedCount);
        Assert.Null(untouched.LockoutEnd);

        using var owner = host.CreateCookieClient("203.0.113.11");
        var me = await owner.LoginAsync(victim.UserName!, TestAccounts.Password);
        Assert.Equal(victim.UserName, me.GetProperty("username").GetString());
    }

    [Fact]
    public async Task Unknown_user_wrong_password_and_locked_out_are_indistinguishable()
    {
        var wrongPassword = await factory.ProvisionAsync();
        var lockedOut = await factory.ProvisionAsync();
        var disabled = await factory.ProvisionAsync();
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var accounts = scope.ServiceProvider.GetRequiredService<AccountService>();
            Assert.True((await accounts.LockAsync(lockedOut.Id)).Succeeded);
            Assert.True((await accounts.DisableAsync(disabled.Id)).Succeeded);
        }

        using var client = factory.CreateCookieClient("198.51.100.20");
        await client.RefreshCsrfAsync();

        var answers = new List<(HttpResponseMessage Response, JsonElement Body)>();
        foreach (var (username, password) in new[]
        {
            (TestAccounts.NewUsername("nobody"), TestAccounts.Password),
            (wrongPassword.UserName!, "Not-The-Password-1"),
            (lockedOut.UserName!, TestAccounts.Password),
            (disabled.UserName!, TestAccounts.Password),
        })
        {
            var response = await client.PostLoginAsync(username, password);
            answers.Add((response, await response.AssertProblemAsync(HttpStatusCode.Unauthorized, "invalid-credentials")));
        }

        var first = Comparable(answers[0].Body);
        foreach (var (response, body) in answers)
        {
            Assert.Equal(first, Comparable(body));
            Assert.Null(response.Headers.RetryAfter);
            Assert.DoesNotContain(response.SetCookieHeaders(), c => c.StartsWith("rushday.auth=", StringComparison.Ordinal));
            response.Dispose();
        }
    }

    [Fact]
    public async Task Demo_account_cannot_sign_in_when_demo_disabled()
    {
        await using var host = factory.Derive(b => b.UseSetting("Demo:Enabled", "false"));
        using var client = host.CreateCookieClient("198.51.100.21");
        await client.RefreshCsrfAsync();

        using var demo = await client.PostLoginAsync("S000002", DemoAccounts.StudentPassword);
        var demoBody = await demo.AssertProblemAsync(HttpStatusCode.Unauthorized, "invalid-credentials");
        using var unknown = await client.PostLoginAsync(TestAccounts.NewUsername("nobody"), DemoAccounts.StudentPassword);
        var unknownBody = await unknown.AssertProblemAsync(HttpStatusCode.Unauthorized, "invalid-credentials");

        Assert.Equal(Comparable(unknownBody), Comparable(demoBody));
    }

    [Fact]
    public async Task Demo_accounts_never_lock()
    {
        const string demoStudent = "S000003";
        var clients = ThreeAddresses.Select(ip => factory.CreateCookieClient(ip)).ToList();
        try
        {
            foreach (var client in clients)
            {
                await client.RefreshCsrfAsync();
            }

            for (var attempt = 0; attempt < 9; attempt++)
            {
                using var failed = await clients[attempt % 3].PostLoginAsync(demoStudent, "Wrong-Password-" + attempt);
                await failed.AssertProblemAsync(HttpStatusCode.Unauthorized, "invalid-credentials");
            }

            var user = await factory.ReadUserAsync(demoStudent);
            Assert.Equal(0, user.AccessFailedCount);
            Assert.Null(user.LockoutEnd);

            var me = await clients[0].LoginAsync(demoStudent, DemoAccounts.StudentPassword);
            Assert.Equal(demoStudent, me.GetProperty("username").GetString());
        }
        finally
        {
            clients.ForEach(c => c.Dispose());
        }
    }

    [Fact]
    public async Task Disabled_user_session_dies_after_validation_interval()
    {
        var user = await factory.ProvisionAsync();
        using var client = await factory.LoginAsync(user.UserName!, TestAccounts.Password);

        // Disabled without rotating the stamp: the validator's own check must end the session.
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RushDayDbContext>();
            var now = factory.Clock.GetUtcNow();
            await db.Users.Where(u => u.Id == user.Id).ExecuteUpdateAsync(s => s.SetProperty(u => u.DisabledAt, now));
        }

        factory.Clock.Advance(TimeSpan.FromSeconds(1));
        using var response = await client.GetAsync("/api/auth/me");
        await response.AssertProblemAsync(HttpStatusCode.Unauthorized, "unauthenticated");
    }

    [Fact]
    public async Task Locked_out_user_session_dies_after_validation_interval()
    {
        var user = await factory.ProvisionAsync();
        using var session = await factory.LoginAsync(user.UserName!, TestAccounts.Password);
        using (var before = await session.GetAsync("/api/auth/me"))
        {
            Assert.Equal(HttpStatusCode.OK, before.StatusCode);
        }

        // An automatic lockout (failures from three addresses) does not rotate the security stamp.
        var stampBefore = (await factory.ReadUserAsync(user.Id)).SecurityStamp;
        var clients = ThreeAddresses.Select(ip => factory.CreateCookieClient(ip)).ToList();
        foreach (var client in clients)
        {
            await client.RefreshCsrfAsync();
        }

        for (var attempt = 0; attempt < 7; attempt++)
        {
            using var failed = await clients[attempt % 3].PostLoginAsync(user.UserName!, "Wrong-Password-" + attempt);
            Assert.Equal(HttpStatusCode.Unauthorized, failed.StatusCode);
        }

        clients.ForEach(c => c.Dispose());
        var locked = await factory.ReadUserAsync(user.Id);
        Assert.NotNull(locked.LockoutEnd);
        Assert.Equal(stampBefore, locked.SecurityStamp);

        factory.Clock.Advance(TimeSpan.FromSeconds(1));
        using var after = await session.GetAsync("/api/auth/me");
        await after.AssertProblemAsync(HttpStatusCode.Unauthorized, "unauthenticated");
    }

    [Fact]
    public async Task Cookie_replayed_after_logout_is_rejected()
    {
        var user = await factory.ProvisionAsync();
        using var client = factory.CreateClient(new() { HandleCookies = false, AllowAutoRedirect = false });

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
        var cookies = $"rushday.csrf={csrfCookie}; rushday.auth={authCookie}";

        using (var me = await SendWithCookies(client, HttpMethod.Get, "/api/auth/me", cookies))
        {
            Assert.Equal(HttpStatusCode.OK, me.StatusCode);
        }

        using (var logout = await SendWithCookies(client, HttpMethod.Post, "/api/auth/logout", cookies, token))
        {
            Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);
        }

        factory.Clock.Advance(TimeSpan.FromSeconds(1));
        using var replay = await SendWithCookies(client, HttpMethod.Get, "/api/auth/me", cookies);
        await replay.AssertProblemAsync(HttpStatusCode.Unauthorized, "unauthenticated");
    }

    [Fact]
    public async Task Must_change_user_can_read_public_status()
    {
        var user = await factory.ProvisionAsync(mustChangePassword: true);
        await using var probe = await ProbeApp.StartAsync(factory);
        using var client = probe.CreateClient();

        var me = await client.LoginAsync(user.UserName!, TestAccounts.Password);
        Assert.True(me.GetProperty("mustChangePassword").GetBoolean());

        using (var status = await client.GetAsync("/api/public/status"))
        {
            Assert.Equal(HttpStatusCode.OK, status.StatusCode);
        }

        using (var current = await client.GetAsync("/api/auth/me"))
        {
            Assert.Equal(HttpStatusCode.OK, current.StatusCode);
        }

        using (var gated = await client.GetAsync(ProbeApp.GatedPath))
        {
            await gated.AssertProblemAsync(HttpStatusCode.Forbidden, "password-change-required");
        }

        // The MFA routes are not exempt from the password gate, which comes first.
        using (var setup = await client.PostAsync("/api/auth/mfa/setup", null))
        {
            await setup.AssertProblemAsync(HttpStatusCode.Forbidden, "password-change-required");
        }

        using (var change = await client.PostAsJsonAsync("/api/auth/change-password", new { currentPassword = TestAccounts.Password, newPassword = TestAccounts.OtherPassword }))
        {
            Assert.Equal(HttpStatusCode.NoContent, change.StatusCode);
        }

        using var after = await client.GetAsync("/api/auth/me");
        Assert.False((await after.ReadJsonAsync()).GetProperty("mustChangePassword").GetBoolean());
        var audit = await ReadAuditAsync(AuditActions.AuthPasswordChanged, user.Id);
        using var details = JsonDocument.Parse(audit.Details!);
        Assert.True(details.RootElement.GetProperty("forced").GetBoolean());
    }

    [Fact]
    public async Task Admin_without_mfa_is_gated()
    {
        var admin = await factory.ProvisionAsync();
        await using var probe = await ProbeApp.StartAsync(factory);
        using var client = probe.CreateClient();

        var me = await client.LoginAsync(admin.UserName!, TestAccounts.Password);
        Assert.True(me.GetProperty("mfaSetupRequired").GetBoolean());
        Assert.False(me.GetProperty("mfaEnabled").GetBoolean());

        using (var gated = await client.GetAsync(ProbeApp.GatedPath))
        {
            await gated.AssertProblemAsync(HttpStatusCode.Forbidden, "mfa-setup-required");
        }

        using (var current = await client.GetAsync("/api/auth/me"))
        {
            Assert.Equal(HttpStatusCode.OK, current.StatusCode);
        }

        using var setup = await client.PostAsync("/api/auth/mfa/setup", null);
        Assert.Equal(HttpStatusCode.OK, setup.StatusCode);
        var sharedKey = (await setup.ReadJsonAsync()).GetProperty("sharedKey").GetString()!;

        using var enable = await client.PostAsJsonAsync("/api/auth/mfa/enable", new { code = Totp.FreshCode(sharedKey) });
        var enabled = await enable.ReadJsonAsync();
        Assert.Equal(HttpStatusCode.OK, enable.StatusCode);
        Assert.False(enabled.GetProperty("mfaSetupRequired").GetBoolean());
        client.UseCsrf(enabled.GetProperty("csrfToken").GetString()!);

        using var open = await client.GetAsync(ProbeApp.GatedPath);
        Assert.Equal(HttpStatusCode.OK, open.StatusCode);
    }

    [Fact]
    public async Task Demo_admin_is_exempt_from_mfa_only_in_demo_mode()
    {
        await using var probe = await ProbeApp.StartAsync(factory);
        using var client = probe.CreateClient();

        var me = await client.LoginAsync(DemoAccounts.AdminUsername, DemoAccounts.AdminPassword);
        Assert.Equal("Admin", me.GetProperty("role").GetString());
        Assert.True(me.GetProperty("isDemo").GetBoolean());
        Assert.False(me.GetProperty("mfaSetupRequired").GetBoolean());

        using (var open = await client.GetAsync(ProbeApp.GatedPath))
        {
            Assert.Equal(HttpStatusCode.OK, open.StatusCode);
        }

        // A demo account can never enrol a factor, so a visitor cannot lock the demo administrator out.
        using (var setup = await client.PostAsync("/api/auth/mfa/setup", null))
        {
            await setup.AssertProblemAsync(HttpStatusCode.Conflict, "demo-account");
        }

        // With demo mode off the exemption is gone, and so is the account.
        await using var off = factory.Derive(b => b.UseSetting("Demo:Enabled", "false"));
        using var offClient = off.CreateCookieClient();
        await offClient.RefreshCsrfAsync();
        using var refused = await offClient.PostLoginAsync(DemoAccounts.AdminUsername, DemoAccounts.AdminPassword);
        await refused.AssertProblemAsync(HttpStatusCode.Unauthorized, "invalid-credentials");
    }

    [Fact]
    public async Task Mfa_login_round_trip()
    {
        var admin = await factory.ProvisionAsync();
        using var client = factory.CreateCookieClient();
        await client.LoginAsync(admin.UserName!, TestAccounts.Password);

        using var setup = await client.PostAsync("/api/auth/mfa/setup", null);
        var setupBody = await setup.ReadJsonAsync();
        Assert.Equal(HttpStatusCode.OK, setup.StatusCode);
        var sharedKey = setupBody.GetProperty("sharedKey").GetString()!;
        var uri = setupBody.GetProperty("otpauthUri").GetString()!;
        Assert.Matches("^([A-Z2-7]{4} )*[A-Z2-7]{1,4}$", sharedKey);
        Assert.StartsWith("otpauth://totp/RushDay:" + admin.UserName, uri, StringComparison.Ordinal);
        Assert.Contains("issuer=RushDay", uri, StringComparison.Ordinal);
        Assert.Contains("secret=" + sharedKey.Replace(" ", string.Empty, StringComparison.Ordinal), uri, StringComparison.OrdinalIgnoreCase);

        var code = Totp.FreshCode(sharedKey);
        using (var wrongEnable = await client.PostAsJsonAsync("/api/auth/mfa/enable", new { code = WrongCode(code) }))
        {
            await wrongEnable.AssertProblemAsync(HttpStatusCode.BadRequest, "invalid-mfa-code");
        }

        using (var enable = await client.PostAsJsonAsync("/api/auth/mfa/enable", new { code }))
        {
            var me = await enable.ReadJsonAsync();
            Assert.Equal(HttpStatusCode.OK, enable.StatusCode);
            Assert.True(me.GetProperty("mfaEnabled").GetBoolean());
            Assert.False(me.GetProperty("mfaSetupRequired").GetBoolean());
            client.UseCsrf(me.GetProperty("csrfToken").GetString()!);
        }

        using (var alreadyEnabled = await client.PostAsync("/api/auth/mfa/setup", null))
        {
            await alreadyEnabled.AssertProblemAsync(HttpStatusCode.Conflict, "mfa-already-enabled");
        }

        using (var logout = await client.PostAsync("/api/auth/logout", null))
        {
            Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);
        }

        await client.RefreshCsrfAsync();
        using var login = await client.PostLoginAsync(admin.UserName!, TestAccounts.Password);
        var challenge = await login.ReadJsonAsync();
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        Assert.True(challenge.GetProperty("mfaRequired").GetBoolean());
        Assert.Contains(login.SetCookieHeaders(), c => c.StartsWith("rushday.mfa=", StringComparison.Ordinal));
        Assert.DoesNotContain(login.SetCookieHeaders(), c => c.StartsWith("rushday.auth=", StringComparison.Ordinal));
        client.UseCsrf(challenge.GetProperty("csrfToken").GetString()!);

        using (var notYet = await client.GetAsync("/api/auth/me"))
        {
            await notYet.AssertProblemAsync(HttpStatusCode.Unauthorized, "unauthenticated");
        }

        var current = Totp.FreshCode(sharedKey);
        using (var wrong = await client.PostAsJsonAsync("/api/auth/mfa/verify", new { code = WrongCode(current) }))
        {
            await wrong.AssertProblemAsync(HttpStatusCode.Unauthorized, "invalid-credentials");
        }

        using var verify = await client.PostAsJsonAsync("/api/auth/mfa/verify", new { code = current });
        var verified = await verify.ReadJsonAsync();
        Assert.Equal(HttpStatusCode.OK, verify.StatusCode);
        Assert.Equal(admin.UserName, verified.GetProperty("username").GetString());
        Assert.True(verified.GetProperty("mfaEnabled").GetBoolean());
        client.UseCsrf(verified.GetProperty("csrfToken").GetString()!);

        using var signedIn = await client.GetAsync("/api/auth/me");
        Assert.Equal(HttpStatusCode.OK, signedIn.StatusCode);
        Assert.NotNull(await ReadAuditAsync(AuditActions.AccountMfaEnabled, admin.Id));
        Assert.NotNull(await ReadAuditAsync(AuditActions.AccountMfaSetupStarted, admin.Id));
    }

    [Fact]
    public async Task Change_password_enforces_the_policy()
    {
        var user = await factory.ProvisionAsync();
        using var client = await factory.LoginAsync(user.UserName!, TestAccounts.Password);

        using (var same = await client.PostAsJsonAsync("/api/auth/change-password", new { currentPassword = TestAccounts.Password, newPassword = TestAccounts.Password }))
        {
            var body = await same.AssertProblemAsync(HttpStatusCode.BadRequest, "weak-password");
            Assert.Equal("same-as-current", body.GetProperty("errors").GetProperty("newPassword")[0].GetString());
        }

        using (var wrong = await client.PostAsJsonAsync("/api/auth/change-password", new { currentPassword = "Not-My-Password-9", newPassword = TestAccounts.OtherPassword }))
        {
            await wrong.AssertProblemAsync(HttpStatusCode.BadRequest, "invalid-current-password");
        }

        using (var weak = await client.PostAsJsonAsync("/api/auth/change-password", new { currentPassword = TestAccounts.Password, newPassword = "short" }))
        {
            var body = await weak.AssertProblemAsync(HttpStatusCode.BadRequest, "weak-password");
            Assert.True(body.GetProperty("errors").GetProperty("newPassword").GetArrayLength() > 0);
        }

        // A wrong current password counts toward lockout.
        Assert.Equal(1, (await factory.ReadUserAsync(user.Id)).AccessFailedCount);

        using var demo = await factory.LoginAsync("S000004", DemoAccounts.StudentPassword);
        using var demoChange = await demo.PostAsJsonAsync("/api/auth/change-password", new { currentPassword = DemoAccounts.StudentPassword, newPassword = TestAccounts.OtherPassword });
        await demoChange.AssertProblemAsync(HttpStatusCode.Conflict, "demo-account");
    }

    [Fact]
    public async Task Sliding_lifetime_ends_an_idle_session()
    {
        var clock = new FakeTimeProvider(RushDayApiFactory.ClockStart);
        await using var host = factory.Derive(clock: clock);
        using var early = await host.LoginAsync("S000005", DemoAccounts.StudentPassword);

        clock.Advance(TimeSpan.FromHours(8) - TimeSpan.FromMinutes(1));
        using (var stillActive = await early.GetAsync("/api/auth/me"))
        {
            Assert.Equal(HttpStatusCode.OK, stillActive.StatusCode);
        }

        // Idle for just over 8 hours, well inside the 12-hour absolute limit: the sliding limit ends it.
        using var late = await host.LoginAsync("S000007", DemoAccounts.StudentPassword);
        clock.Advance(TimeSpan.FromHours(8) + TimeSpan.FromMinutes(1));
        using var idle = await late.GetAsync("/api/auth/me");
        await idle.AssertProblemAsync(HttpStatusCode.Unauthorized, "unauthenticated");
    }

    [Fact]
    public async Task Absolute_lifetime_ends_an_active_session()
    {
        var clock = new FakeTimeProvider(RushDayApiFactory.ClockStart);
        await using var host = factory.Derive(clock: clock);
        using var client = await host.LoginAsync("S000006", DemoAccounts.StudentPassword);

        // Active every two hours: the sliding limit never trips, the absolute one does after 12 hours.
        for (var hour = 2; hour <= 12; hour += 2)
        {
            clock.Advance(TimeSpan.FromHours(2));
            using var active = await client.GetAsync("/api/auth/me");
            Assert.True(active.StatusCode == HttpStatusCode.OK, $"Session ended early at {hour} h.");
        }

        clock.Advance(TimeSpan.FromMinutes(1));
        using var expired = await client.GetAsync("/api/auth/me");
        await expired.AssertProblemAsync(HttpStatusCode.Unauthorized, "unauthenticated");
    }

    [Fact]
    public async Task Admin_sessions_idle_out_after_sixty_minutes()
    {
        var clock = new FakeTimeProvider(RushDayApiFactory.ClockStart);
        await using var host = factory.Derive(clock: clock);
        using var client = await host.LoginAsync(DemoAccounts.AdminUsername, DemoAccounts.AdminPassword);

        clock.Advance(TimeSpan.FromMinutes(59));
        using (var active = await client.GetAsync("/api/auth/me"))
        {
            Assert.Equal(HttpStatusCode.OK, active.StatusCode);
        }

        clock.Advance(TimeSpan.FromMinutes(61));
        using var idle = await client.GetAsync("/api/auth/me");
        await idle.AssertProblemAsync(HttpStatusCode.Unauthorized, "unauthenticated");
    }

    private static string Comparable(JsonElement problem) =>
        string.Join('|', "type", "title", "status", "detail", "instance").Split('|')
            .Select(p => p + "=" + (problem.TryGetProperty(p, out var v) ? v.ToString() : "<absent>"))
            .Aggregate((a, b) => a + ";" + b);

    private static string WrongCode(string code) => code == "000000" ? "111111" : "000000";

    private static async Task<HttpResponseMessage> SendWithCookies(HttpClient client, HttpMethod method, string path, string cookies, string? csrfToken = null)
    {
        using var request = new HttpRequestMessage(method, path);
        request.Headers.Add("Cookie", cookies);
        if (csrfToken is not null)
        {
            request.Headers.Add(TestClients.CsrfHeader, csrfToken);
        }

        return await client.SendAsync(request);
    }

    private async Task<AuditEvent> ReadAuditAsync(string action, Guid userId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<RushDayDbContext>();
        return await db.AuditEvents.AsNoTracking().SingleAsync(a => a.Action == action && a.SubjectId == userId.ToString());
    }
}
