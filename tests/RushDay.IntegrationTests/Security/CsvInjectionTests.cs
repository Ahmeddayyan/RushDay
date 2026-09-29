using System.Net;
using System.Text;
using RushDay.Infrastructure.Audit;
using RushDay.Infrastructure.Queries;
using RushDay.IntegrationTests.Staff;

namespace RushDay.IntegrationTests.Security;

/// <summary>
/// OWASP CSV/formula injection (03-security.md section 5): a cell whose first character is <c>=</c>, <c>+</c>,
/// <c>-</c>, <c>@</c>, a tab or a carriage return must never reach a spreadsheet unescaped. <see cref="AuditCsvWriter"/>
/// prefixes such a cell with a single quote before applying ordinary RFC 4180 quoting. Proven first at the unit level
/// (the writer's own methods), then end to end through a real CSV-producing endpoint: the lecturer roster export,
/// where a student's <c>fullName</c> is attacker-controlled free text. Module <c>YV9101</c>, lecturer <c>L00027</c>,
/// student <c>S990001</c>.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class CsvInjectionTests(RushDayApiFactory factory)
{
    [Theory]
    [InlineData("=1+2", "\"'=1+2\"")]
    [InlineData("+1", "\"'+1\"")]
    [InlineData("-1", "\"'-1\"")]
    [InlineData("@SUM(A1)", "\"'@SUM(A1)\"")]
    [InlineData("\tcmd", "\"'\tcmd\"")]
    [InlineData("\rcmd", "\"'\rcmd\"")]
    public void Cell_prefixes_every_formula_trigger_character(string value, string expected) =>
        Assert.Equal(expected, AuditCsvWriter.Cell(value));

    [Fact]
    public void Cell_combines_the_injection_prefix_with_rfc4180_quoting()
    {
        // A value that both triggers the formula prefix (leading '=') and needs RFC 4180 quoting of its own comma
        // and embedded quote: '=1,"2"' becomes, character by character, a leading "'", the original text with every
        // '"' doubled, the whole thing wrapped in one more pair of quotes.
        const string quote = "\"";
        var expected = quote + "'=1," + quote + quote + "2" + quote + quote + quote;

        Assert.Equal(expected, AuditCsvWriter.Cell("=1,\"2\""));
    }

    [Fact]
    public void Cell_quotes_a_comma_and_an_embedded_quote_without_a_trigger_character()
    {
        // No leading trigger character: no prefix, but the comma stays safely inside quotes and the embedded quote
        // still doubles (plain RFC 4180, the baseline the injection rule sits on top of).
        Assert.Equal("\"a,b\"", AuditCsvWriter.Cell("a,b"));
        Assert.Equal("\"say \"\"hi\"\"\"", AuditCsvWriter.Cell("say \"hi\""));
        Assert.Equal("\"line1\r\nline2\"", AuditCsvWriter.Cell("line1\r\nline2"));
    }

    [Fact]
    public void Cell_is_unaffected_by_a_trigger_character_that_is_not_first()
    {
        // Only the first character matters: '1=2' is an ordinary number-ish string, not a formula.
        Assert.Equal("\"1=2\"", AuditCsvWriter.Cell("1=2"));
        Assert.Equal("\"a-b\"", AuditCsvWriter.Cell("a-b"));
    }

    [Fact]
    public async Task WriteAsync_neutralises_injection_in_every_column_it_writes()
    {
        var row = new AuditEventRow(
            Id: Guid.NewGuid(),
            OccurredAt: new DateTimeOffset(2026, 9, 29, 10, 0, 0, TimeSpan.Zero),
            ActorUsername: "=cmd|calc",
            ActorRole: "Admin",
            Action: "student.created",
            SubjectType: "Student",
            SubjectId: "S990002",
            StudentNumber: "S990002",
            ModuleCode: "+YV9101",
            Details: "{\"note\":\"-2+2\"}",
            RequestId: "@req-1");

        await using var stream = new MemoryStream();
        await AuditCsvWriter.WriteAsync(stream, OneRowAsync(row), truncatedAt: null);
        var text = Encoding.UTF8.GetString(stream.ToArray());
        var lines = text.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);

        Assert.Equal(2, lines.Length);
        Assert.Equal(
            "\"occurredAt\",\"actorUsername\",\"actorRole\",\"action\",\"subjectType\",\"subjectId\",\"studentNumber\",\"moduleCode\",\"details\",\"requestId\"",
            lines[0]);

        // Every column that carried a trigger character is prefixed; none is passed through raw.
        Assert.Contains(",\"'=cmd|calc\",", lines[1], StringComparison.Ordinal);
        Assert.DoesNotContain(",\"=cmd|calc\",", lines[1], StringComparison.Ordinal);
        Assert.Contains(",\"'+YV9101\",", lines[1], StringComparison.Ordinal);
        Assert.Contains(",\"'@req-1\"", lines[1], StringComparison.Ordinal);
        // The JSON details start with a brace: never itself a trigger character, so it is carried unprefixed
        // (its own quotes still double for RFC 4180).
        Assert.Contains(",\"{\"\"note\"\":\"\"-2+2\"\"}\",", lines[1], StringComparison.Ordinal);
    }

    /// <summary>
    /// The real end-to-end path: an admin-created student's attacker-controlled <c>fullName</c> flows, unmodified by
    /// any application code, into the lecturer roster CSV (<c>LecturerEndpoints.RosterCsvAsync</c>, which builds the
    /// line with <see cref="AuditCsvWriter.Line"/> directly from the query row).
    /// </summary>
    [Fact]
    public async Task Roster_csv_neutralises_a_students_full_name()
    {
        const string code = "YV9101";
        const string leader = "L00027";
        const string studentNumber = "S990001";
        const string maliciousName = "=1+1";

        using var admin = await factory.DemoAdminAsync();
        await admin.CreateModuleAsync(code);
        await admin.AssignAsync(code, leader);
        await admin.PostJsonAsync(
            "/api/admin/students",
            new { studentNumber, fullName = maliciousName, programme = "BSc Computer Science", yearOfStudy = 1 },
            HttpStatusCode.Created);
        await admin.OverrideEnrolAsync(studentNumber, code);

        using var lecturer = await factory.LecturerAsync(leader);
        using var response = await lecturer.GetAsync($"/api/lecturer/modules/{code}/roster.csv");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/csv", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("utf-8", response.Content.Headers.ContentType?.CharSet);
        Assert.Equal($"attachment; filename=\"roster-{code}.csv\"", response.Content.Headers.ContentDisposition?.ToString());

        var lines = (await response.Content.ReadAsStringAsync()).Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal("\"studentNumber\",\"fullName\",\"programme\",\"yearOfStudy\",\"status\",\"enrolledAt\",\"withdrawnAt\"", lines[0]);

        var row = Assert.Single(lines[1..], l => l.StartsWith($"\"{studentNumber}\",", StringComparison.Ordinal));
        Assert.Contains($",\"'{maliciousName}\",", row, StringComparison.Ordinal);
        Assert.DoesNotContain($",\"{maliciousName}\",", row, StringComparison.Ordinal);
    }

    private static async IAsyncEnumerable<AuditEventRow> OneRowAsync(AuditEventRow row)
    {
        await Task.Yield();
        yield return row;
    }
}
