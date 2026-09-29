using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.DependencyInjection;
using RushDay.Infrastructure.Caching;
using RushDay.IntegrationTests.Student;

namespace RushDay.IntegrationTests.Staff;

/// <summary>
/// The joint pass's platform items that surfaced in the S6 review: the MFA code's digit class (J4), versioned
/// invalidation of the windows, publications and lecturer-module caches (J5), and planner statistics after a start
/// that migrated or backfilled (review S6 E16).
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class PlatformFixTests(RushDayApiFactory factory)
{
    /// <summary>
    /// J4: an MFA code is six ASCII digits; Arabic-Indic or full-width digits are 400 <c>validation</c> before any
    /// lookup. Before the fix <c>\d</c> admitted them and the request reached the challenge (401).
    /// </summary>
    [Theory]
    [InlineData("١٢٣٤٥٦")]
    [InlineData("１２３４５６")]
    public async Task Mfa_codes_take_ascii_digits_only(string code)
    {
        using var client = factory.CreateCookieClient();
        client.UseCsrf(await client.RefreshCsrfAsync());

        using var verify = await client.PostAsJsonAsync("/api/auth/mfa/verify", new { code });
        await verify.AssertProblemAsync(HttpStatusCode.BadRequest, "validation");
    }

    /// <summary>
    /// J5: invalidating <c>windows:all</c>, <c>publications:brief</c> and <c>lecturer-modules:{id}</c> advances the
    /// key's generation (04 section 4), so a fill that read the rows before the change stores into a key nobody reads
    /// (the mechanism <c>CacheGenerationTests</c> proves). Before the fix these three used <c>RemoveAsync</c>, which an
    /// in-flight fill outlives by a lifetime: a lecturer removed from a module could keep it for 60 s.
    /// </summary>
    [Fact]
    public async Task Windows_publications_and_lecturer_modules_invalidate_by_generation()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var cache = services.GetRequiredService<HybridCache>();
        var lecturerId = Guid.CreateVersion7();
        var windowsBefore = CacheGenerations.CurrentKey(cache, CacheKeys.Windows);
        var briefBefore = CacheGenerations.CurrentKey(cache, CacheKeys.PublicationsBrief);
        var lecturerBefore = CacheGenerations.CurrentKey(cache, CacheKeys.LecturerModules(lecturerId));

        await services.GetRequiredService<EnrolmentWindowCache>().InvalidateAsync();
        await services.GetRequiredService<PublicationCache>().InvalidateAsync();
        await services.GetRequiredService<LecturerModuleCache>().InvalidateAsync(lecturerId);

        Assert.NotEqual(windowsBefore, CacheGenerations.CurrentKey(cache, CacheKeys.Windows));
        Assert.NotEqual(briefBefore, CacheGenerations.CurrentKey(cache, CacheKeys.PublicationsBrief));
        Assert.NotEqual(lecturerBefore, CacheGenerations.CurrentKey(cache, CacheKeys.LecturerModules(lecturerId)));

        // And they still serve: the next read fills the new generation.
        Assert.NotEmpty(await services.GetRequiredService<EnrolmentWindowCache>().GetAllAsync());
        Assert.Empty(await services.GetRequiredService<LecturerModuleCache>().GetCodesAsync(lecturerId));
    }

    /// <summary>
    /// E16: the start that created this database migrated it and ran the backfills, so it refreshed the planner's
    /// statistics on the tables they rewrote (<c>last_analyze</c> is set by a manual ANALYZE only, never by
    /// autovacuum). Before the fix nothing ran ANALYZE and the admin results query sorted on disk until autovacuum
    /// caught up.
    /// </summary>
    [Fact]
    public async Task The_start_that_migrated_refreshed_planner_statistics()
    {
        foreach (var table in new[] { "grades", "enrolments", "modules", "users" })
        {
            var analyzed = await DbProbe.ScalarAsync<bool>(factory, "SELECT last_analyze IS NOT NULL FROM pg_stat_user_tables WHERE relname = @t", ("t", table));
            Assert.True(analyzed, $"{table} was never analyzed");
        }
    }
}
