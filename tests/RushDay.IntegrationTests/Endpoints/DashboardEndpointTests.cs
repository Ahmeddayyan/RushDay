using System.Net;
using System.Net.Http.Json;
using RushDay.Api.Contracts;

namespace RushDay.IntegrationTests.Endpoints;

[Collection(ApiCollection.Name)]
public sealed class DashboardEndpointTests(RushDayApiFactory factory)
{
    [Fact]
    public async Task Returns_the_dashboard_for_a_seeded_student()
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/students/S000001/dashboard");

        response.EnsureSuccessStatusCode();
        var dashboard = await response.Content.ReadFromJsonAsync<DashboardResponse>();
        Assert.NotNull(dashboard);
        Assert.Equal("S000001", dashboard.StudentNumber);
        Assert.Equal(RushDayApiFactory.SeedOptions.AutumnModulesPerStudent, dashboard.Modules.Count);
        Assert.Equal(dashboard.Modules.Count, dashboard.Results.Count);
        Assert.NotNull(dashboard.WeightedAverage);
        Assert.NotNull(dashboard.Classification);
    }

    [Fact]
    public async Task Returns_404_for_an_unknown_student()
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/students/S999999/dashboard");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
