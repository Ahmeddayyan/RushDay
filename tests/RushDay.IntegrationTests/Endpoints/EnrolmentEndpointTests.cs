using System.Net;
using System.Net.Http.Json;
using RushDay.Api.Contracts;
using RushDay.Infrastructure.Seeding;

namespace RushDay.IntegrationTests.Endpoints;

[Collection(ApiCollection.Name)]
public sealed class EnrolmentEndpointTests(RushDayApiFactory factory)
{
    [Fact]
    public async Task Enrols_once_then_rejects_the_duplicate()
    {
        using var client = factory.CreateClient();
        var request = new EnrolRequest(DatabaseSeeder.HotModuleCode);

        var first = await client.PostAsJsonAsync("/students/S000002/enrolments", request);
        var second = await client.PostAsJsonAsync("/students/S000002/enrolments", request);

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        var created = await first.Content.ReadFromJsonAsync<EnrolmentResponse>();
        Assert.NotNull(created);
        Assert.Equal(DatabaseSeeder.HotModuleCode, created.ModuleCode);

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }

    [Fact]
    public async Task Module_detail_reflects_enrolments()
    {
        using var client = factory.CreateClient();
        await client.PostAsJsonAsync("/students/S000003/enrolments", new EnrolRequest(DatabaseSeeder.HotModuleCode));

        var detail = await client.GetFromJsonAsync<ModuleDetail>($"/modules/{DatabaseSeeder.HotModuleCode}");

        Assert.NotNull(detail);
        Assert.Equal(DatabaseSeeder.HotModuleCapacity, detail.Capacity);
        Assert.True(detail.Enrolled >= 1);
        Assert.Equal(detail.Capacity - detail.Enrolled, detail.PlacesRemaining);
    }

    [Fact]
    public async Task Returns_404_for_an_unknown_module()
    {
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/students/S000004/enrolments", new EnrolRequest("XX9999"));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
