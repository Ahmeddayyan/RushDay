using System.Net;
using RushDay.Infrastructure.Seeding;

namespace RushDay.IntegrationTests.Auth;

/// <summary>The X-CSRF-TOKEN header on every non-GET under /api, login included (02-api.md section 3, T3).</summary>
[Collection(ApiCollection.Name)]
public sealed class AntiforgeryTests(RushDayApiFactory factory)
{
    [Fact]
    public async Task Post_without_header_is_400()
    {
        using var client = factory.CreateCookieClient();
        await client.RefreshCsrfAsync();
        client.DefaultRequestHeaders.Remove(TestClients.CsrfHeader);

        // Login itself is protected (login CSRF).
        using var login = await client.PostLoginAsync("S000008", DemoAccounts.StudentPassword);
        await login.AssertProblemAsync(HttpStatusCode.BadRequest, "antiforgery");

        using var signedIn = await factory.LoginAsync("S000008", DemoAccounts.StudentPassword);
        signedIn.DefaultRequestHeaders.Remove(TestClients.CsrfHeader);
        using var logout = await signedIn.PostAsync("/api/auth/logout", null);
        await logout.AssertProblemAsync(HttpStatusCode.BadRequest, "antiforgery");
    }

    [Fact]
    public async Task A_forged_token_is_400()
    {
        using var client = factory.CreateCookieClient();
        await client.RefreshCsrfAsync();
        client.UseCsrf("forged-token");

        using var response = await client.PostLoginAsync("S000008", DemoAccounts.StudentPassword);
        await response.AssertProblemAsync(HttpStatusCode.BadRequest, "antiforgery");
    }

    [Fact]
    public async Task Token_is_bound_to_the_signed_in_identity()
    {
        using var client = factory.CreateCookieClient();
        var anonymous = await client.RefreshCsrfAsync();
        await client.LoginAsync("S000009", DemoAccounts.StudentPassword);

        // The anonymous token no longer matches the principal; the SPA refreshes after login.
        client.UseCsrf(anonymous);
        using var stale = await client.PostAsync("/api/auth/logout", null);
        await stale.AssertProblemAsync(HttpStatusCode.BadRequest, "antiforgery");

        using var me = await client.GetAsync("/api/auth/me");
        client.UseCsrf((await me.ReadJsonAsync()).GetProperty("csrfToken").GetString()!);
        using var fresh = await client.PostAsync("/api/auth/logout", null);
        Assert.Equal(HttpStatusCode.NoContent, fresh.StatusCode);
    }

    [Fact]
    public async Task Token_travels_in_json_and_the_cookie_is_not_readable()
    {
        using var client = factory.CreateCookieClient();

        using var response = await client.GetAsync("/api/auth/csrf");
        var token = (await response.ReadJsonAsync()).GetProperty("csrfToken").GetString();
        var cookie = Assert.Single(response.SetCookieHeaders(), c => c.StartsWith("rushday.csrf=", StringComparison.Ordinal));

        Assert.False(string.IsNullOrEmpty(token));
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=strict", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("path=/", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(token!, cookie, StringComparison.Ordinal);
    }
}
