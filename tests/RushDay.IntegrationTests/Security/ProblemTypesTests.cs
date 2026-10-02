using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.RegularExpressions;
using RushDay.Api.Security;

namespace RushDay.IntegrationTests.Security;

/// <summary>The closed slug catalogue and the ProblemDetails conventions of 02-api.md section 6 (D16).</summary>
[Collection(ApiCollection.Name)]
public sealed partial class ProblemTypesTests(RushDayApiFactory factory)
{
    /// <summary>The table of 02-api.md section 6, row by row.</summary>
    private static readonly Dictionary<int, string[]> SpecTable = new()
    {
        [400] = ["validation", "antiforgery", "invalid-current-password", "weak-password", "invalid-mfa-code"],
        [401] = ["unauthenticated", "invalid-credentials"],
        [403] = ["forbidden", "not-your-module", "not-module-leader", "password-change-required", "mfa-setup-required"],
        [404] = ["not-found", "module-not-found", "student-not-found", "lecturer-not-found", "account-not-found", "window-not-found", "publication-not-found", "announcement-not-found", "grade-not-found", "not-enrolled"],
        [405] = ["method-not-allowed"],
        [409] = ["already-enrolled", "module-full", "module-inactive", "enrolment-window-closed", "withdrawal-deadline-passed", "results-exist", "student-left", "module-locked", "module-not-submitted", "already-submitted", "nothing-to-submit", "stale-mark", "nothing-to-publish", "publication-live", "publication-scheduled", "username-taken", "principal-has-account", "principal-left", "module-code-taken", "student-number-taken", "staff-number-taken", "window-exists", "demo-account", "mfa-already-enabled"],
        [413] = ["payload-too-large"],
        [415] = ["unsupported-media-type"],
        [422] = ["credit-limit-exceeded", "marks-incomplete", "not-enrolled-students", "capacity-below-enrolled", "semester-change-with-enrolments", "publish-too-far-ahead", "invalid-lecturer-assignment", "window-dates-invalid", "self-lockout", "role-principal-mismatch"],
        [429] = ["rate-limited"],
        [500] = ["internal-error"],
        [503] = ["server-busy", "timeout"],
    };

    [Fact]
    public void Catalogue_equals_the_spec_table()
    {
        var expected = SpecTable.SelectMany(row => row.Value.Select(slug => (slug, row.Key))).ToDictionary(p => p.slug, p => p.Key);

        Assert.Equal(63, expected.Count);
        Assert.Equal(expected.Keys.Order(StringComparer.Ordinal), ProblemTypes.All.Order(StringComparer.Ordinal));
        foreach (var (slug, status) in expected)
        {
            Assert.Equal(status, ProblemTypes.StatusBySlug[slug]);
        }
    }

    [Fact]
    public void Catalogue_equals_the_table_in_the_committed_spec()
    {
        var spec = FindSpec();
        if (spec is null)
        {
            return; // Outside a repository checkout; the hand-copied table above still holds.
        }

        var lines = File.ReadAllLines(spec);
        var header = Array.FindIndex(lines, l => l.StartsWith("| Status | Slug | Where |", StringComparison.Ordinal));
        Assert.True(header >= 0, "The slug table of 02-api.md section 6 was not found.");

        var slugs = new HashSet<string>(StringComparer.Ordinal);
        foreach (var line in lines.Skip(header + 2).TakeWhile(l => l.StartsWith('|')))
        {
            var slugColumn = line.Split('|')[2];
            foreach (Match match in BacktickedName().Matches(Parenthesised().Replace(slugColumn, string.Empty)))
            {
                slugs.Add(match.Groups[1].Value);
            }
        }

        Assert.Equal(63, slugs.Count);
        Assert.Equal(slugs.Order(StringComparer.Ordinal), ProblemTypes.All.Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task Framework_problems_carry_catalogue_types_and_trace_ids()
    {
        using var client = factory.CreateCookieClient();

        using var notFound = await client.GetAsync("/api/does/not/exist");
        var body = await notFound.AssertProblemAsync(HttpStatusCode.NotFound, "not-found");
        Assert.Equal(404, body.GetProperty("status").GetInt32());
        Assert.Equal("/api/does/not/exist", body.GetProperty("instance").GetString());
        Assert.False(string.IsNullOrEmpty(body.GetProperty("title").GetString()));
        Assert.False(string.IsNullOrEmpty(body.GetProperty("detail").GetString()));

        // A route that exists but not for this method or body type still falls to the /api catch-all: a JSON 404,
        // never an empty body, a redirect or index.html.
        await client.RefreshCsrfAsync();
        using var wrongMethod = await client.PutAsync("/api/auth/login", null);
        await wrongMethod.AssertProblemAsync(HttpStatusCode.NotFound, "not-found");
        using var formBody = await client.PostAsync("/api/auth/login", new StringContent("username=a&password=b", Encoding.UTF8, "application/x-www-form-urlencoded"));
        await formBody.AssertProblemAsync(HttpStatusCode.NotFound, "not-found");
    }

    [Theory]
    [InlineData(400, "validation")]
    [InlineData(401, "unauthenticated")]
    [InlineData(403, "forbidden")]
    [InlineData(404, "not-found")]
    [InlineData(405, "method-not-allowed")]
    [InlineData(413, "payload-too-large")]
    [InlineData(415, "unsupported-media-type")]
    [InlineData(429, "rate-limited")]
    [InlineData(500, "internal-error")]
    [InlineData(503, "server-busy")]
    public void Framework_statuses_map_to_their_slugs(int status, string slug)
    {
        var problem = new Microsoft.AspNetCore.Mvc.ProblemDetails { Status = status, Type = "https://tools.ietf.org/html/rfc9110", Detail = "System.Exception: internals" };
        var http = new Microsoft.AspNetCore.Http.DefaultHttpContext();
        http.Request.Path = "/api/x";

        ProblemDetailsCustomizer.Apply(http, problem);

        Assert.Equal("urn:rushday:" + slug, problem.Type);
        Assert.DoesNotContain("System.Exception", problem.Detail, StringComparison.Ordinal);
        Assert.Equal("/api/x", problem.Instance);
        Assert.True(problem.Extensions.ContainsKey("traceId"));
    }

    [Fact]
    public async Task Invalid_input_is_a_camel_cased_validation_problem()
    {
        using var client = factory.CreateCookieClient();
        await client.RefreshCsrfAsync();

        using var response = await client.PostAsJsonAsync("/api/auth/login", new { username = string.Empty, password = new string('x', 129) });

        var body = await response.AssertProblemAsync(HttpStatusCode.BadRequest, "validation");
        var errors = body.GetProperty("errors");
        Assert.True(errors.TryGetProperty("username", out _), errors.ToString());
        Assert.True(errors.TryGetProperty("password", out _), errors.ToString());
    }

    /// <summary>A body that does not bind is the caller's mistake in every environment: 400, never a 500 (the main host is Development).</summary>
    [Theory]
    [InlineData("{\"username\": \"s000001\", \"password\": ")]
    [InlineData("")]
    [InlineData("{\"username\": 5, \"password\": \"x\"}")]
    [InlineData("[1, 2]")]
    public async Task Unbindable_bodies_are_validation_problems(string body)
    {
        using var client = factory.CreateCookieClient();
        await client.RefreshCsrfAsync();

        using var response = await client.PostAsync("/api/auth/login", new StringContent(body, Encoding.UTF8, "application/json"));

        await response.AssertProblemAsync(HttpStatusCode.BadRequest, "validation");
    }

    [Fact]
    public async Task A_mfa_code_must_be_six_digits()
    {
        using var client = factory.CreateCookieClient();
        await client.RefreshCsrfAsync();

        using var response = await client.PostAsJsonAsync("/api/auth/mfa/verify", new { code = "12ab56" });

        var body = await response.AssertProblemAsync(HttpStatusCode.BadRequest, "validation");
        Assert.True(body.GetProperty("errors").TryGetProperty("code", out _));
    }

    private static string? FindSpec()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, "docs", "spec", "02-api.md");
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    [GeneratedRegex(@"\([^)]*\)")]
    private static partial Regex Parenthesised();

    [GeneratedRegex("`([a-z][a-z-]*)`")]
    private static partial Regex BacktickedName();
}
