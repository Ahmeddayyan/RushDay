using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using RushDay.Api.Options;
using RushDay.Infrastructure.Seeding;

namespace RushDay.IntegrationTests.Endpoints;

/// <summary><c>GET /api/public/status</c> (02-api.md section 8.1, T16).</summary>
[Collection(ApiCollection.Name)]
public sealed class PublicStatusTests(RushDayApiFactory factory)
{
    [Fact]
    public async Task Demo_accounts_are_listed_in_demo_mode()
    {
        using var client = factory.CreateCookieClient();

        var status = await GetStatusAsync(client);

        var accounts = status.GetProperty("demo").GetProperty("accounts").EnumerateArray().ToList();
        Assert.Equal(3, accounts.Count);
        Assert.Equal(["Student", "Lecturer", "Admin"], accounts.Select(a => a.GetProperty("role").GetString()));
        Assert.Equal([DemoAccounts.StudentUsername, DemoAccounts.LecturerUsername, DemoAccounts.AdminUsername], accounts.Select(a => a.GetProperty("username").GetString()));
        Assert.Equal([DemoAccounts.StudentPassword, DemoAccounts.LecturerPassword, DemoAccounts.AdminPassword], accounts.Select(a => a.GetProperty("password").GetString()));
        Assert.Equal(DemoAccounts.StudentHint, accounts[0].GetProperty("hint").GetString());
    }

    [Fact]
    public async Task Demo_null_when_disabled()
    {
        await using var host = factory.Derive(b => b.UseSetting("Demo:Enabled", "false"));
        using var client = host.CreateCookieClient();

        var status = await GetStatusAsync(client);

        Assert.Equal(JsonValueKind.Null, status.GetProperty("demo").ValueKind);
    }

    [Fact]
    public async Task Status_describes_the_institution_the_year_and_the_publications()
    {
        using var client = factory.CreateCookieClient();

        var status = await GetStatusAsync(client);

        var institution = status.GetProperty("institution");
        Assert.Equal("RushDay Demo University", institution.GetProperty("name").GetString());
        Assert.Equal("RushDay", institution.GetProperty("shortName").GetString());
        Assert.Equal("Europe/London", institution.GetProperty("timeZone").GetString());
        Assert.Equal(JsonValueKind.Null, institution.GetProperty("privacyNoticeUrl").ValueKind);
        Assert.Equal(BrandingOptions.DefaultResultsFootnote, institution.GetProperty("resultsFootnote").GetString());
        Assert.Equal(JsonValueKind.Null, institution.GetProperty("support").ValueKind);

        Assert.Equal("2026/27", status.GetProperty("academicYear").GetString());
        Assert.Equal("autumn", status.GetProperty("currentSemester").GetString());

        // The seeded autumn 2025/26 publication (D24: in the past) is live; nothing is scheduled.
        Assert.Equal(JsonValueKind.Null, status.GetProperty("nextPublication").ValueKind);
        var latest = status.GetProperty("latestPublication");
        Assert.Equal("2025/26", latest.GetProperty("academicYear").GetString());
        Assert.Equal("autumn", latest.GetProperty("semester").GetString());
        Assert.Equal("2026-01-26T09:00:00.000Z", latest.GetProperty("publishAt").GetString());
        Assert.Equal("live", latest.GetProperty("state").GetString());

        var windows = status.GetProperty("enrolmentWindows").EnumerateArray().ToList();
        Assert.Equal(["autumn", "spring"], windows.Select(w => w.GetProperty("semester").GetString()));
        Assert.All(windows, w => Assert.Equal("open", w.GetProperty("state").GetString()));
        Assert.All(windows, w => Assert.Equal("2026/27", w.GetProperty("academicYear").GetString()));
        Assert.Equal("2026-10-02T17:00:00.000Z", windows[0].GetProperty("closesAt").GetString());
        Assert.Matches(@"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}\.\d{3}Z$", status.GetProperty("serverTime").GetString());
    }

    [Fact]
    public async Task Branding_values_are_read_at_request_time()
    {
        await using var host = factory.Derive(b =>
        {
            b.UseSetting("Branding:PrivacyNoticeUrl", "https://example.ac.uk/privacy");
            b.UseSetting("Branding:ResultsFootnote", "Speak to your tutor.");
        });
        using var client = host.CreateCookieClient();

        var institution = (await GetStatusAsync(client)).GetProperty("institution");

        Assert.Equal("https://example.ac.uk/privacy", institution.GetProperty("privacyNoticeUrl").GetString());
        Assert.Equal("Speak to your tutor.", institution.GetProperty("resultsFootnote").GetString());
    }

    [Fact]
    public async Task Signed_in_callers_are_served_the_same_uncookied_response()
    {
        using var anonymous = factory.CreateCookieClient();
        using var signedIn = await factory.LoginAsync("S000013", DemoAccounts.StudentPassword);

        using var first = await anonymous.GetAsync("/api/public/status");

        // A second later a freshly built body would carry another serverTime: equal bodies (and an Age header) mean
        // the signed-in caller was served the cached one.
        factory.Clock.Advance(TimeSpan.FromSeconds(1));
        using var second = await signedIn.GetAsync("/api/public/status");

        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.Empty(first.SetCookieHeaders());
        Assert.Equal("no-store", first.Headers.CacheControl?.ToString());
        Assert.Equal(await first.Content.ReadAsStringAsync(), await second.Content.ReadAsStringAsync());
        Assert.NotNull(second.Headers.Age);
    }

    [Fact]
    public async Task Status_is_output_cached_for_ten_seconds()
    {
        using var client = factory.CreateCookieClient();

        var first = (await GetStatusAsync(client)).GetProperty("serverTime").GetString();
        factory.Clock.Advance(TimeSpan.FromSeconds(1));
        var second = (await GetStatusAsync(client)).GetProperty("serverTime").GetString();

        Assert.Equal(first, second);
    }

    private static async Task<JsonElement> GetStatusAsync(HttpClient client)
    {
        using var response = await client.GetAsync("/api/public/status");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        return await response.ReadJsonAsync();
    }
}
