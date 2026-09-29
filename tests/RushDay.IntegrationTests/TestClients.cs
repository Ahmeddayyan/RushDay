using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Mvc.Testing.Handlers;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;
using RushDay.Api.Auth;
using RushDay.Api.Observability;
using RushDay.Api.Startup;
using RushDay.Domain.Users;
using RushDay.Infrastructure.Accounts;
using RushDay.Infrastructure.Caching;
using RushDay.Infrastructure.Identity;
using RushDay.Infrastructure.Persistence;

namespace RushDay.IntegrationTests;

/// <summary>
/// HTTP helpers for the session flow of 02-api.md sections 2–3: cookie-carrying clients, the anonymous CSRF token,
/// sign-in that swaps in the token bound to the signed-in identity, and an MFA-aware variant.
/// </summary>
public static class TestClients
{
    public const string CsrfHeader = "X-CSRF-TOKEN";
    public const string ForwardedForHeader = "X-Forwarded-For";

    public static readonly Uri LocalHttp = new("http://localhost");
    public static readonly Uri ProductionHttps = new("https://rushday.test");

    /// <summary>A client with a cookie container, no redirects and, optionally, a synthetic client address.</summary>
    public static HttpClient CreateCookieClient(this WebApplicationFactory<Program> factory, string? forwardedFor = null, Uri? baseAddress = null)
    {
        ArgumentNullException.ThrowIfNull(factory);
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = true,
            BaseAddress = baseAddress ?? LocalHttp,
        });
        if (forwardedFor is not null)
        {
            client.DefaultRequestHeaders.Add(ForwardedForHeader, forwardedFor);
        }

        return client;
    }

    /// <summary>GET /api/auth/csrf and send the token on every later request.</summary>
    public static async Task<string> RefreshCsrfAsync(this HttpClient client)
    {
        ArgumentNullException.ThrowIfNull(client);
        using var response = await client.GetAsync("/api/auth/csrf");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var token = (await response.ReadJsonAsync()).GetProperty("csrfToken").GetString()!;
        client.UseCsrf(token);
        return token;
    }

    public static void UseCsrf(this HttpClient client, string token)
    {
        ArgumentNullException.ThrowIfNull(client);
        client.DefaultRequestHeaders.Remove(CsrfHeader);
        client.DefaultRequestHeaders.Add(CsrfHeader, token);
    }

    public static Task<HttpResponseMessage> PostLoginAsync(this HttpClient client, string username, string password) =>
        client.PostAsJsonAsync("/api/auth/login", new { username, password });

    /// <summary>A signed-in client: csrf → login (must answer <c>Me</c>) → the token bound to the identity.</summary>
    public static async Task<HttpClient> LoginAsync(this WebApplicationFactory<Program> factory, string username, string password, string? forwardedFor = null, Uri? baseAddress = null)
    {
        var client = factory.CreateCookieClient(forwardedFor, baseAddress);
        await client.LoginAsync(username, password);
        return client;
    }

    /// <summary>Signs <paramref name="client"/> in and returns <c>Me</c>.</summary>
    public static async Task<JsonElement> LoginAsync(this HttpClient client, string username, string password)
    {
        await client.RefreshCsrfAsync();
        using var response = await client.PostLoginAsync(username, password);
        var body = await response.ReadJsonAsync();
        Assert.True(response.StatusCode == HttpStatusCode.OK, $"Login as {username} answered {(int)response.StatusCode}: {body}");
        Assert.False(body.TryGetProperty("mfaRequired", out _), "Login answered an MFA challenge; use LoginWithMfaAsync.");
        client.UseCsrf(body.GetProperty("csrfToken").GetString()!);
        return body;
    }

    /// <summary>
    /// The MFA-aware variant: password → <c>{ mfaRequired }</c> → a TOTP code from <paramref name="sharedKey"/> → <c>Me</c>.
    /// The code is <see cref="Totp.FreshCode"/>: the server refuses a time step it has already accepted.
    /// </summary>
    public static async Task<JsonElement> LoginWithMfaAsync(this HttpClient client, string username, string password, string sharedKey)
    {
        await client.RefreshCsrfAsync();
        using var login = await client.PostLoginAsync(username, password);
        var challenge = await login.ReadJsonAsync();
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        Assert.True(challenge.GetProperty("mfaRequired").GetBoolean());
        client.UseCsrf(challenge.GetProperty("csrfToken").GetString()!);

        using var verify = await client.PostAsJsonAsync("/api/auth/mfa/verify", new { code = Totp.FreshCode(sharedKey) });
        var me = await verify.ReadJsonAsync();
        Assert.True(verify.StatusCode == HttpStatusCode.OK, $"MFA verify answered {(int)verify.StatusCode}: {me}");
        client.UseCsrf(me.GetProperty("csrfToken").GetString()!);
        return me;
    }

    public static async Task<JsonElement> ReadJsonAsync(this HttpResponseMessage response)
    {
        ArgumentNullException.ThrowIfNull(response);
        var text = await response.Content.ReadAsStringAsync();
        return string.IsNullOrEmpty(text) ? default : JsonDocument.Parse(text).RootElement.Clone();
    }

    /// <summary>Asserts a ProblemDetails response of the closed catalogue and returns its body.</summary>
    public static async Task<JsonElement> AssertProblemAsync(this HttpResponseMessage response, HttpStatusCode status, string slug)
    {
        ArgumentNullException.ThrowIfNull(response);
        var body = await response.ReadJsonAsync();
        Assert.True(response.StatusCode == status, $"Expected {(int)status} {slug}, got {(int)response.StatusCode}: {body}");
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("urn:rushday:" + slug, body.GetProperty("type").GetString());
        Assert.False(string.IsNullOrEmpty(body.GetProperty("traceId").GetString()));
        return body;
    }

    /// <summary>The <c>name=value</c> pairs of every Set-Cookie of a response, for cookie-less replay tests.</summary>
    public static IReadOnlyDictionary<string, string> SetCookies(this HttpResponseMessage response)
    {
        ArgumentNullException.ThrowIfNull(response);
        var cookies = new Dictionary<string, string>(StringComparer.Ordinal);
        if (response.Headers.TryGetValues("Set-Cookie", out var values))
        {
            foreach (var value in values)
            {
                var pair = value.Split(';', 2)[0];
                var equals = pair.IndexOf('=', StringComparison.Ordinal);
                cookies[pair[..equals]] = pair[(equals + 1)..];
            }
        }

        return cookies;
    }

    public static IReadOnlyList<string> SetCookieHeaders(this HttpResponseMessage response) =>
        response.Headers.TryGetValues("Set-Cookie", out var values) ? [.. values] : [];
}

/// <summary>
/// RFC 6238 TOTP (SHA-1, 30-second steps, 6 digits) for tests. The server's provider uses the real clock (never the
/// factory's fake one), accepts ±2 steps and refuses a step it has already accepted for the account.
/// </summary>
public static class Totp
{
    private const int StepSeconds = 30;

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, long> LastIssued = new(StringComparer.Ordinal);

    public static string Code(string sharedKey, DateTimeOffset at) => CodeAt(sharedKey, at.ToUnixTimeSeconds() / StepSeconds);

    /// <summary>
    /// A code for a time step later than any this helper has handed out for <paramref name="sharedKey"/>, still inside
    /// the server's ±2-step window, so successive calls never replay a step the server may have accepted.
    /// </summary>
    public static string FreshCode(string sharedKey)
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds() / StepSeconds;
        var step = LastIssued.AddOrUpdate(sharedKey, now, (_, last) => Math.Max(now, last + 1));
        if (step > now + 2)
        {
            throw new InvalidOperationException("More than three fresh codes for one key within a step; the server would refuse the next.");
        }

        return CodeAt(sharedKey, step);
    }

    private static string CodeAt(string sharedKey, long counter)
    {
        var key = Base32Decode(sharedKey.Replace(" ", string.Empty, StringComparison.Ordinal).ToUpperInvariant());
        var message = BitConverter.GetBytes(counter);
        if (BitConverter.IsLittleEndian)
        {
            Array.Reverse(message);
        }

        var hash = HMACSHA1.HashData(key, message);
        var offset = hash[^1] & 0x0F;
        var binary = ((hash[offset] & 0x7F) << 24) | (hash[offset + 1] << 16) | (hash[offset + 2] << 8) | hash[offset + 3];
        return (binary % 1_000_000).ToString("D6", System.Globalization.CultureInfo.InvariantCulture);
    }

    private static byte[] Base32Decode(string input)
    {
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        var output = new List<byte>(input.Length * 5 / 8);
        int buffer = 0, bits = 0;
        foreach (var c in input.TrimEnd('='))
        {
            buffer = (buffer << 5) | alphabet.IndexOf(c, StringComparison.Ordinal);
            bits += 5;
            if (bits >= 8)
            {
                output.Add((byte)(buffer >> (bits - 8)));
                bits -= 8;
            }
        }

        return [.. output];
    }
}

/// <summary>
/// The production composition (services, pipeline and the <c>/api</c> group with its filters) plus a few probe
/// endpoints, so the gates and the authorization policies can be observed on ordinary authenticated routes before
/// stages S4 and S6 add the real ones. It shares the factory's database, key ring and clock.
/// </summary>
public sealed class ProbeApp : IAsyncDisposable
{
    /// <summary>Authenticated by the fallback policy only: neither anonymous nor gate-exempt.</summary>
    public const string GatedPath = "/api/probe/gated";

    public const string StudentPath = "/api/probe/student";
    public const string AdminPath = "/api/probe/admin";

    /// <summary>Anonymous; throws an ordinary exception (500 <c>internal-error</c>).</summary>
    public const string ThrowPath = "/api/probe/throw";

    /// <summary>Anonymous; throws a transient <see cref="NpgsqlException"/> (503 <c>server-busy</c>).</summary>
    public const string TransientPath = "/api/probe/transient";

    /// <summary>Authenticated; reads the settings cache, runs one query and answers the request's command count.</summary>
    public const string CommandsPath = "/api/probe/commands";

    private readonly WebApplication _app;

    private ProbeApp(WebApplication app) => _app = app;

    public IServiceProvider Services => _app.Services;

    /// <summary>Behind <c>LecturerOnly</c> and <c>TeachesModule</c>, like S6's <c>/api/lecturer/modules/{code}/*</c>.</summary>
    public static string ModulePath(string code) => "/api/probe/modules/" + code;

    /// <param name="factory">Supplies the database, settings and clock.</param>
    /// <param name="webRoot">A web root to serve instead of none (the test output has no <c>wwwroot</c>).</param>
    /// <param name="environment">Development unless stated; Production also gets the test KEK.</param>
    /// <param name="configure">Runs after the app's own registrations (fake logging, overrides).</param>
    public static async Task<ProbeApp> StartAsync(RushDayApiFactory factory, string? webRoot = null, string? environment = null, Action<WebApplicationBuilder>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(factory);
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = environment ?? Environments.Development,
            ContentRootPath = AppContext.BaseDirectory,
            WebRootPath = webRoot,
        });
        builder.WebHost.UseTestServer();
        builder.Configuration.AddInMemoryCollection(factory.Settings(startupWork: false));
        if (builder.Environment.IsProduction())
        {
            builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DataProtection:KeyEncryptionKey"] = RushDayApiFactory.TestKeyEncryptionKey,
                ["Demo:PublicDemoAcknowledged"] = "true",
            });
        }

        builder.AddRushDayServices();
        builder.Services.AddSingleton<TimeProvider>(factory.Clock);
        configure?.Invoke(builder);

        var app = builder.Build();
        app.UseRushDayPipeline();
        var api = app.MapRushDayEndpoints();
        api.MapGet("/probe/gated", () => TypedResults.Ok(new { reached = true }));
        api.MapGet("/probe/student", () => TypedResults.Ok(new { reached = true })).RequireAuthorization(Policies.StudentOnly);
        api.MapGet("/probe/admin", () => TypedResults.Ok(new { reached = true })).RequireAuthorization(Policies.AdminOnly);
        api.MapGroup("/probe/modules").RequireAuthorization(Policies.LecturerOnly)
            .MapGet("/{code:regex(^[A-Z]{{2}}\\d{{4}}$)}", (string code) => TypedResults.Ok(new { code }))
            .RequireAuthorization(Policies.TeachesModule);
        api.MapGet("/probe/throw", IResult () => throw new InvalidOperationException("probe failure")).AllowAnonymous();
        api.MapGet("/probe/transient", IResult () => throw new NpgsqlException("probe transient failure", new TimeoutException())).AllowAnonymous();
        api.MapGet("/probe/commands", async (SettingsCache settings, RushDayDbContext db, DbCommandCounter counter, CancellationToken cancellationToken) =>
        {
            _ = await settings.GetAsync(cancellationToken);
            _ = await db.Modules.CountAsync(cancellationToken);
            return TypedResults.Ok(new { commands = counter.Count });
        });

        // What S6's account routes will do, so the demo-actor rule can be observed through a real session.
        var accounts = api.MapGroup("/probe/accounts").RequireAuthorization(Policies.AdminOnly);
        accounts.MapPost("/", async (AccountService service, CancellationToken cancellationToken) =>
        {
            var result = await service.ProvisionAsync(new ProvisionAccountRequest(TestAccounts.NewUsername("v"), "Visitor Account", RushDayRoles.Admin), cancellationToken);
            return TypedResults.Ok(new { id = result.Value!.User.Id, isDemo = result.Value.User.IsDemo, mustChangePassword = result.Value.User.MustChangePassword });
        });
        accounts.MapPost("/{id:guid}/lock", async (Guid id, AccountService service, CancellationToken cancellationToken) =>
        {
            var result = await service.LockAsync(id, cancellationToken);
            return TypedResults.Ok(new { error = result.Error.ToString() });
        });
        await app.StartAsync();
        return new ProbeApp(app);
    }

    public HttpClient CreateClient(Uri? baseAddress = null) =>
        new(new CookieContainerHandler { InnerHandler = _app.GetTestServer().CreateHandler() }) { BaseAddress = baseAddress ?? TestClients.LocalHttp };

    public async Task<HttpClient> LoginAsync(string username, string password)
    {
        var client = CreateClient();
        await client.LoginAsync(username, password);
        return client;
    }

    public async ValueTask DisposeAsync()
    {
        await _app.StopAsync();
        await _app.DisposeAsync();
    }
}

/// <summary>Accounts created through the real <see cref="AccountService"/>, with unique usernames.</summary>
public static class TestAccounts
{
    /// <summary>Passes the policy: 24 characters, no username, no product name, not on the blocklist.</summary>
    public const string Password = "Correct-Horse-Battery-42";

    public const string OtherPassword = "Violet-Staple-Harbour-77";

    public static string NewUsername(string prefix = "t") => prefix + Guid.NewGuid().ToString("N")[..12];

    /// <summary>Provisions a non-demo account (an administrator unless told otherwise) with a known password.</summary>
    public static async Task<ApplicationUser> ProvisionAsync(this WebApplicationFactory<Program> factory, string role = RushDayRoles.Admin, bool mustChangePassword = false, string password = Password)
    {
        ArgumentNullException.ThrowIfNull(factory);
        await using var scope = factory.Services.CreateAsyncScope();
        var accounts = scope.ServiceProvider.GetRequiredService<AccountService>();
        var result = await accounts.ProvisionAsync(new ProvisionAccountRequest(NewUsername(), "Test Account", role, TemporaryPassword: password));
        Assert.True(result.Succeeded, $"Provisioning failed: {result.Error} {string.Join(", ", result.Codes)}");

        var user = result.Value!.User;
        if (!mustChangePassword)
        {
            var db = scope.ServiceProvider.GetRequiredService<RushDayDbContext>();
            await db.Users.Where(u => u.Id == user.Id).ExecuteUpdateAsync(s => s.SetProperty(u => u.MustChangePassword, false));
            user.MustChangePassword = false;
        }

        return user;
    }

    public static async Task<ApplicationUser> ReadUserAsync(this WebApplicationFactory<Program> factory, Guid id)
    {
        ArgumentNullException.ThrowIfNull(factory);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<RushDayDbContext>();
        return await db.Users.AsNoTracking().SingleAsync(u => u.Id == id);
    }

    public static async Task<ApplicationUser> ReadUserAsync(this WebApplicationFactory<Program> factory, string username)
    {
        ArgumentNullException.ThrowIfNull(factory);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<RushDayDbContext>();
        var normalized = username.ToUpperInvariant();
        return await db.Users.AsNoTracking().SingleAsync(u => u.NormalizedUserName == normalized);
    }
}
