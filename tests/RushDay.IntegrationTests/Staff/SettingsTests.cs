using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RushDay.Domain.Audit;
using RushDay.IntegrationTests.Student;

namespace RushDay.IntegrationTests.Staff;

/// <summary>
/// Academic settings and enrolment windows (02-api.md section 8.5): a change of academic year recomputes
/// <c>enrolled_count</c> for the new year in the same transaction (D28) and the caches follow; other changes do not
/// touch the counts; the time zone and support URL are validated; windows are created, changed and deleted with their
/// date rule and audited. The settings row is restored exactly (the public status tests read it), and windows are
/// only ever made for years nothing else uses.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class SettingsTests(RushDayApiFactory factory)
{
    [Fact]
    public async Task A_year_change_recomputes_enrolled_count()
    {
        using var admin = await factory.DemoAdminAsync();
        var original = await admin.GetJsonAsync("/api/admin/settings");
        Assert.Equal(StaffData.CurrentYear, original.GetProperty("academicYear").GetString());

        await admin.CreateModuleAsync("YT9101");
        await admin.OverrideEnrolAsync("S000098", "YT9101");
        await admin.OverrideEnrolAsync("S000099", "YT9101");
        Assert.Equal(2, EnrolledCount(await admin.GetJsonAsync("/api/modules/YT9101")));
        var cs3001 = await factory.CountsAsync("CS3001");

        try
        {
            var changed = await admin.PutJsonAsync("/api/admin/settings", With(original, academicYear: "2027/28"));
            Assert.Equal("2027/28", changed.GetProperty("academicYear").GetString());

            // Nobody is enrolled for 2027/28 yet: every place is free, and the cached catalogue says so at once.
            Assert.Equal(0, EnrolledCount(await admin.GetJsonAsync("/api/modules/YT9101")));
            var catalogue = (await admin.GetJsonAsync("/api/modules")).EnumerateArray().Single(m => m.GetProperty("code").GetString() == "YT9101");
            Assert.Equal(0, EnrolledCount(catalogue));
            Assert.Equal(0, (await factory.CountsAsync("CS3001")).EnrolledCount);

            var audit = (await factory.AuditAsync(AuditActions.SettingsChanged)).First();
            var details = StaffData.DetailsOf(audit);
            Assert.Equal((StaffData.CurrentYear, "2027/28"), (details.GetProperty("before").GetProperty("academicYear").GetString(), details.GetProperty("after").GetProperty("academicYear").GetString()));
        }
        finally
        {
            await admin.PutJsonAsync("/api/admin/settings", With(original));
        }

        Assert.Equal(2, EnrolledCount(await admin.GetJsonAsync("/api/modules/YT9101")));
        Assert.Equal(cs3001, await factory.CountsAsync("CS3001"));
        var restored = await admin.GetJsonAsync("/api/admin/settings");
        foreach (var field in new[] { "academicYear", "currentSemester", "institutionName", "institutionShortName", "timeZone", "supportEmail", "supportUrl" })
        {
            Assert.Equal(original.GetProperty(field).ToString(), restored.GetProperty(field).ToString());
        }
    }

    [Fact]
    public async Task Other_changes_leave_the_counts_and_are_validated()
    {
        using var admin = await factory.DemoAdminAsync();
        var original = await admin.GetJsonAsync("/api/admin/settings");
        await admin.CreateModuleAsync("YT9201");
        await factory.WithDbAsync(db => db.Modules.Where(m => m.Code == "YT9201").ExecuteUpdateAsync(s => s.SetProperty(m => m.EnrolledCount, 9)));

        try
        {
            var changed = await admin.PutJsonAsync("/api/admin/settings", With(original, supportEmail: "office@example.test", supportUrl: "https://example.test/help", timeZone: "America/Argentina/Buenos_Aires"));
            Assert.Equal(("office@example.test", "https://example.test/help"), (changed.GetProperty("supportEmail").GetString(), changed.GetProperty("supportUrl").GetString()));
            Assert.Equal(9, (await factory.CountsAsync("YT9201")).EnrolledCount);

            foreach (var invalid in new[]
            {
                With(original, academicYear: "2026-27"),
                With(original, academicYear: "٢٠٢٦/27"),
                With(original, timeZone: "Not a zone"),
                With(original, supportUrl: "http://example.test/help"),
                With(original, supportUrl: "/relative"),
                With(original, supportEmail: "nobody"),
                With(original, currentSemester: "summer"),
                With(original, institutionName: ""),
            })
            {
                using var response = await admin.PutAsJsonAsync("/api/admin/settings", invalid);
                await response.AssertProblemAsync(HttpStatusCode.BadRequest, "validation");
            }
        }
        finally
        {
            await admin.PutJsonAsync("/api/admin/settings", With(original));
            await factory.WithDbAsync(db => db.Modules.Where(m => m.Code == "YT9201").ExecuteUpdateAsync(s => s.SetProperty(m => m.EnrolledCount, 0)));
        }

        var restored = await admin.GetJsonAsync("/api/admin/settings");
        Assert.Equal(JsonValueKind.Null, restored.GetProperty("supportEmail").ValueKind);
        Assert.Equal(JsonValueKind.Null, restored.GetProperty("supportUrl").ValueKind);
        Assert.Equal(original.GetProperty("timeZone").GetString(), restored.GetProperty("timeZone").GetString());
    }

    [Fact]
    public async Task Windows_are_created_changed_and_deleted_with_their_date_rule()
    {
        using var admin = await factory.DemoAdminAsync();
        var opens = new DateTimeOffset(2031, 9, 1, 9, 0, 0, TimeSpan.Zero);
        var window = new { academicYear = "2031/32", semester = "autumn", opensAt = opens, closesAt = opens.AddDays(14), withdrawalDeadlineAt = opens.AddDays(30) };

        var created = await admin.PostJsonAsync("/api/admin/enrolment-windows", window, HttpStatusCode.Created);
        var id = created.GetProperty("id").GetGuid();
        Assert.Equal(("2031/32", "autumn", "notYetOpen"), (created.GetProperty("academicYear").GetString(), created.GetProperty("semester").GetString(), created.GetProperty("state").GetString()));

        try
        {
            var exists = await admin.PostJsonAsync("/api/admin/enrolment-windows", window, HttpStatusCode.Conflict);
            Assert.Equal("urn:rushday:window-exists", exists.GetProperty("type").GetString());
            var badDates = await admin.PostJsonAsync("/api/admin/enrolment-windows", window with { academicYear = "2032/33", closesAt = opens.AddDays(-1) }, HttpStatusCode.UnprocessableEntity);
            Assert.Equal("urn:rushday:window-dates-invalid", badDates.GetProperty("type").GetString());
            using (var notADate = await admin.PostAsJsonAsync("/api/admin/enrolment-windows", new { academicYear = "2032/33", semester = "autumn", opensAt = "01/09/2031 09:00", closesAt = opens, withdrawalDeadlineAt = opens }))
            {
                await notADate.AssertProblemAsync(HttpStatusCode.BadRequest, "validation");
            }

            var list = await admin.GetJsonAsync("/api/admin/enrolment-windows");
            Assert.Equal("2031/32", list[0].GetProperty("academicYear").GetString());

            var updated = await admin.PutJsonAsync($"/api/admin/enrolment-windows/{id}", new { opensAt = opens, closesAt = opens.AddDays(20), withdrawalDeadlineAt = opens.AddDays(20) });
            Assert.Equal(opens.AddDays(20), updated.GetProperty("closesAt").GetDateTimeOffset());
            await admin.PutJsonAsync($"/api/admin/enrolment-windows/{id}", new { opensAt = opens, closesAt = opens.AddDays(20), withdrawalDeadlineAt = opens.AddDays(19) }, HttpStatusCode.UnprocessableEntity);
            await admin.PutJsonAsync($"/api/admin/enrolment-windows/{Guid.NewGuid()}", new { opensAt = opens, closesAt = opens.AddDays(20), withdrawalDeadlineAt = opens.AddDays(20) }, HttpStatusCode.NotFound);

            var audit = Assert.Single(await factory.AuditAsync(AuditActions.WindowUpdated, id.ToString()));
            var details = StaffData.DetailsOf(audit);
            Assert.Equal(("2031/32", "autumn"), (details.GetProperty("academicYear").GetString(), details.GetProperty("semester").GetString()));
            Assert.Equal("2031-09-15T09:00:00.000Z", details.GetProperty("before").GetProperty("closesAt").GetString());
            Assert.Equal("2031-09-21T09:00:00.000Z", details.GetProperty("after").GetProperty("closesAt").GetString());
        }
        finally
        {
            using var delete = await admin.DeleteAsync($"/api/admin/enrolment-windows/{id}");
            Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);
        }

        Assert.Single(await factory.AuditAsync(AuditActions.WindowCreated, id.ToString()));
        Assert.Single(await factory.AuditAsync(AuditActions.WindowDeleted, id.ToString()));
        using var gone = await admin.DeleteAsync($"/api/admin/enrolment-windows/{id}");
        await gone.AssertProblemAsync(HttpStatusCode.NotFound, "window-not-found");
    }

    private static int EnrolledCount(JsonElement module) => module.GetProperty("enrolledCount").GetInt32();

    private static Dictionary<string, object?> With(
        JsonElement original,
        string? academicYear = null,
        string? supportEmail = null,
        string? supportUrl = null,
        string? timeZone = null,
        string? currentSemester = null,
        string? institutionName = null) => new()
        {
            ["academicYear"] = academicYear ?? original.GetProperty("academicYear").GetString(),
            ["currentSemester"] = currentSemester ?? original.GetProperty("currentSemester").GetString(),
            ["institutionName"] = institutionName ?? original.GetProperty("institutionName").GetString(),
            ["institutionShortName"] = original.GetProperty("institutionShortName").GetString(),
            ["timeZone"] = timeZone ?? original.GetProperty("timeZone").GetString(),
            ["supportEmail"] = supportEmail ?? (original.GetProperty("supportEmail").ValueKind == JsonValueKind.Null ? null : original.GetProperty("supportEmail").GetString()),
            ["supportUrl"] = supportUrl ?? (original.GetProperty("supportUrl").ValueKind == JsonValueKind.Null ? null : original.GetProperty("supportUrl").GetString()),
        };
}
