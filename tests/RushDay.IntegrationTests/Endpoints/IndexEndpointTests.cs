using System.Net;
using RushDay.Api.Endpoints;
using RushDay.Api.Hosting;

namespace RushDay.IntegrationTests.Endpoints;

/// <summary>SPA hosting (05-frontend.md section 4), the /api index (00 section 1) and the removed routes (02 section 8.6).</summary>
[Collection(ApiCollection.Name)]
public sealed class IndexEndpointTests(RushDayApiFactory factory)
{
    [Theory]
    [InlineData("/")]
    [InlineData("/login")]
    [InlineData("/student/results")]
    public async Task Anonymous_page_requests_get_the_shell_or_the_not_built_notice(string path)
    {
        using var client = factory.CreateCookieClient();

        using var response = await client.GetAsync(path);
        var body = await response.Content.ReadAsStringAsync();

        // CI builds the front end into wwwroot first; a local run without a web build answers the 503 notice.
        if (response.StatusCode == HttpStatusCode.OK)
        {
            Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
            Assert.Contains("<div id=\"root\">", body, StringComparison.Ordinal);
            Assert.Equal("no-cache", response.Headers.CacheControl?.ToString());
        }
        else
        {
            Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
            Assert.Equal("text/plain", response.Content.Headers.ContentType?.MediaType);
            Assert.Equal(SpaHosting.NotBuiltMessage, body);
        }
    }

    [Theory]
    [InlineData("/api/nope")]
    [InlineData("/api/students/S000001/dashboard")]
    [InlineData("/api/modules/CS3099/enrol")]
    public async Task Unknown_api_path_is_a_json_404_even_anonymously(string path)
    {
        using var client = factory.CreateCookieClient();

        using var response = await client.GetAsync(path);

        await response.AssertProblemAsync(HttpStatusCode.NotFound, "not-found");
    }

    [Theory]
    [InlineData("GET", "/students/S000001/dashboard")]
    [InlineData("GET", "/modules")]
    [InlineData("GET", "/modules/CS3099")]
    [InlineData("GET", "/health")]
    [InlineData("GET", "/openapi/v1.json")]
    public async Task Legacy_v0_routes_are_gone(string method, string path)
    {
        using var client = factory.CreateCookieClient();

        using var response = await client.SendAsync(new HttpRequestMessage(new HttpMethod(method), path));

        // Non-/api paths belong to the SPA: its shell (or the not-built notice), never v0's JSON.
        Assert.NotEqual("application/json", response.Content.Headers.ContentType?.MediaType);
        Assert.True(response.StatusCode is HttpStatusCode.OK or HttpStatusCode.ServiceUnavailable or HttpStatusCode.NotFound, $"{path}: {(int)response.StatusCode}");
    }

    [Fact]
    public async Task Register_route_does_not_exist()
    {
        using var client = factory.CreateCookieClient();
        await client.RefreshCsrfAsync();

        using var response = await client.PostAsync("/api/auth/register", null);

        // The /api fallback is anonymous: 404 ProblemDetails, not 401 and not 400.
        await response.AssertProblemAsync(HttpStatusCode.NotFound, "not-found");
    }

    [Fact]
    public async Task Api_index_tells_the_story()
    {
        using var client = factory.CreateCookieClient();

        using var response = await client.GetAsync("/api");
        var index = await response.ReadJsonAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("RushDay", index.GetProperty("name").GetString());
        Assert.Equal("I built RushDay because my own university's portal fell over every results day and enrolment window. I wanted to understand why that happens and to build a portal that doesn't crash under the same load.", index.GetProperty("story").GetString());
        Assert.Equal(IndexEndpoints.Story, index.GetProperty("story").GetString());
        Assert.Equal("local", index.GetProperty("commit").GetString());
        Assert.Equal("Development", index.GetProperty("environment").GetString());

        var links = index.GetProperty("links");
        Assert.Equal("/api/health/live", links.GetProperty("health").GetString());
        Assert.Equal("/api/health/ready", links.GetProperty("ready").GetString());
        Assert.Equal("/api/public/status", links.GetProperty("status").GetString());
        Assert.Equal("/api/auth/login", links.GetProperty("login").GetString());
        Assert.Equal("https://github.com/Ahmeddayyan/RushDay", links.GetProperty("github").GetString());
        Assert.Equal("/api/openapi/v1.json", links.GetProperty("openapi").GetString());
    }

    [Fact]
    public async Task Openapi_served_in_development()
    {
        using var client = factory.CreateCookieClient();

        using var response = await client.GetAsync("/api/openapi/v1.json");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("/api/auth/login", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Openapi_absent_in_production()
    {
        await using var production = factory.Production();
        using var client = production.CreateCookieClient(baseAddress: TestClients.ProductionHttps);

        using var index = await client.GetAsync("/api");
        var links = (await index.ReadJsonAsync()).GetProperty("links");
        Assert.False(links.TryGetProperty("openapi", out _));
        Assert.Equal("Production", (await index.ReadJsonAsync()).GetProperty("environment").GetString());

        using var document = await client.GetAsync("/api/openapi/v1.json");
        await document.AssertProblemAsync(HttpStatusCode.NotFound, "not-found");
    }
}
