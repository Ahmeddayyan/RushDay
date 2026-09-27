using System.Net.Http.Json;
using System.Text.Json;

namespace RushDay.IntegrationTests.Endpoints;

[Collection(ApiCollection.Name)]
public sealed class IndexEndpointTests(RushDayApiFactory factory)
{
    [Fact]
    public async Task Root_serves_the_dashboard_page()
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/");

        response.EnsureSuccessStatusCode();
        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("<title>RushDay</title>", html);
    }

    [Fact]
    public async Task Api_index_lists_links_and_commit()
    {
        using var client = factory.CreateClient();

        using var doc = await client.GetFromJsonAsync<JsonDocument>("/api");

        Assert.NotNull(doc);
        Assert.Equal("RushDay", doc.RootElement.GetProperty("name").GetString());
        Assert.True(doc.RootElement.TryGetProperty("commit", out _));
        Assert.True(doc.RootElement.GetProperty("links").TryGetProperty("dashboard", out _));
    }
}
