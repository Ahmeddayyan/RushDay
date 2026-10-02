using System.Net;
using System.Net.Http.Json;
using RushDay.Domain.Grades;
using RushDay.IntegrationTests.Student;

namespace RushDay.IntegrationTests.Staff;

/// <summary>
/// A lecturer reaches only the modules they teach (02-api.md sections 4 and 8.4): another module's roster, marks and
/// announcements are 403 <c>not-your-module</c> whatever the code in the URL; through their own module's routes an
/// announcement of another module or of the university does not exist (404), and whatever they create is an
/// announcement of their module; a body can only name students of their own module. Modules <c>YL####</c>.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class LecturerIsolationTests(RushDayApiFactory factory)
{
    private const string Mine = "YL9601";
    private const string Theirs = "YL9602";

    [Fact]
    public async Task Another_modules_data_is_out_of_reach_by_url_and_by_body()
    {
        using var admin = await factory.DemoAdminAsync();
        await admin.CreateModuleAsync(Mine);
        await admin.CreateModuleAsync(Theirs);
        await admin.AssignAsync(Mine, "L00035");
        await admin.AssignAsync(Theirs, "L00036");
        await admin.OverrideEnrolAsync("S000090", Mine);
        await admin.OverrideEnrolAsync("S000099", Theirs);
        using var mine = await factory.LecturerAsync("L00035");
        using var theirs = await factory.LecturerAsync("L00036");
        await theirs.SaveMarksAsync(Theirs, [new { studentNumber = "S000099", mark = 88, version = (int?)null }]);

        foreach (var path in new[] { "roster", "roster.csv", "marks", "announcements" })
        {
            using var response = await mine.GetAsync($"/api/lecturer/modules/{Theirs}/{path}");
            await response.AssertProblemAsync(HttpStatusCode.Forbidden, "not-your-module");
        }

        using (var put = await mine.PutAsJsonAsync($"/api/lecturer/modules/{Theirs}/marks", new { rows = new[] { new { studentNumber = "S000099", mark = 1, version = 1 } } }))
        {
            await put.AssertProblemAsync(HttpStatusCode.Forbidden, "not-your-module");
        }

        using (var submit = await mine.PostAsync($"/api/lecturer/modules/{Theirs}/marks/submit", null))
        {
            await submit.AssertProblemAsync(HttpStatusCode.Forbidden, "not-your-module");
        }

        // Through their own module, a student of another module cannot be named: nothing about them is written or read.
        using (var foreignStudent = await mine.PutAsJsonAsync($"/api/lecturer/modules/{Mine}/marks", new { rows = new[] { new { studentNumber = "S000099", mark = 1, version = 1 } } }))
        {
            await foreignStudent.AssertProblemAsync(HttpStatusCode.UnprocessableEntity, "not-enrolled-students");
        }

        Assert.Equal((88, GradeStatus.Draft), ((await factory.GradeAsync("S000099", Theirs)).Mark!.Value, (await factory.GradeAsync("S000099", Theirs)).Status));
        var ownSheet = await mine.GetJsonAsync($"/api/lecturer/modules/{Mine}/marks?q=S000099");
        Assert.Equal(0, ownSheet.GetProperty("total").GetInt32());

        // And none of the administrator's routes.
        foreach (var path in new[] { "/api/admin/modules", $"/api/admin/modules/{Theirs}/marks", "/api/admin/students/S000099", "/api/admin/audit" })
        {
            using var response = await mine.GetAsync(path);
            await response.AssertProblemAsync(HttpStatusCode.Forbidden, "forbidden");
        }
    }

    [Fact]
    public async Task Announcements_of_other_scopes_cannot_be_created_edited_or_deleted()
    {
        using var admin = await factory.DemoAdminAsync();
        await admin.CreateModuleAsync("YL9701");
        await admin.CreateModuleAsync("YL9702");
        await admin.AssignAsync("YL9701", "L00037");
        await admin.AssignAsync("YL9702", "L00038");
        using var mine = await factory.LecturerAsync("L00037");
        using var theirs = await factory.LecturerAsync("L00038");

        var university = await admin.PostJsonAsync("/api/admin/announcements", new { title = "University notice", body = "For everyone." }, HttpStatusCode.Created);
        var universityId = university.GetProperty("id").GetGuid();
        var foreign = await theirs.PostJsonAsync("/api/lecturer/modules/YL9702/announcements", new { title = "Their notice", body = "Theirs." }, HttpStatusCode.Created);
        var foreignId = foreign.GetProperty("id").GetGuid();

        try
        {
            // Whatever a lecturer creates belongs to their module, never the university.
            var created = await mine.PostJsonAsync("/api/lecturer/modules/YL9701/announcements", new { title = "My notice", body = "Mine.", pinned = true }, HttpStatusCode.Created);
            Assert.Equal(("module", "YL9701", true), (created.GetProperty("scope").GetString(), created.GetProperty("moduleCode").GetString(), created.GetProperty("pinned").GetBoolean()));
            var ownList = await mine.GetJsonAsync("/api/lecturer/modules/YL9701/announcements");
            Assert.Equal(["My notice"], ownList.Strings("title"));

            // Through the lecturer's own module, the university's and the other module's announcements do not exist.
            foreach (var id in new[] { universityId, foreignId })
            {
                using var edit = await mine.PutAsJsonAsync($"/api/lecturer/modules/YL9701/announcements/{id}", new { title = "Hijacked", body = "Not mine." });
                await edit.AssertProblemAsync(HttpStatusCode.NotFound, "announcement-not-found");
                using var delete = await mine.DeleteAsync($"/api/lecturer/modules/YL9701/announcements/{id}");
                await delete.AssertProblemAsync(HttpStatusCode.NotFound, "announcement-not-found");
            }

            // Through the other module's routes, the policy refuses first.
            using (var edit = await mine.PutAsJsonAsync($"/api/lecturer/modules/YL9702/announcements/{foreignId}", new { title = "Hijacked", body = "Not mine." }))
            {
                await edit.AssertProblemAsync(HttpStatusCode.Forbidden, "not-your-module");
            }

            using (var create = await mine.PostAsJsonAsync("/api/lecturer/modules/YL9702/announcements", new { title = "Planted", body = "Not mine." }))
            {
                await create.AssertProblemAsync(HttpStatusCode.Forbidden, "not-your-module");
            }

            // And the administrator's university routes are closed to lecturers.
            using (var adminCreate = await mine.PostAsJsonAsync("/api/admin/announcements", new { title = "Planted", body = "Not mine." }))
            {
                await adminCreate.AssertProblemAsync(HttpStatusCode.Forbidden, "forbidden");
            }

            using (var adminDelete = await mine.DeleteAsync($"/api/admin/announcements/{universityId}"))
            {
                await adminDelete.AssertProblemAsync(HttpStatusCode.Forbidden, "forbidden");
            }

            var all = await admin.GetJsonAsync("/api/admin/announcements");
            Assert.Contains("University notice", all.Strings("title"));
            Assert.Contains("Their notice", all.Strings("title"));
            Assert.DoesNotContain("Hijacked", all.Strings("title"));
            Assert.DoesNotContain("Planted", all.Strings("title"));

            // The lecturer edits and deletes their own.
            var ownId = created.GetProperty("id").GetGuid();
            var edited = await mine.PutJsonAsync($"/api/lecturer/modules/YL9701/announcements/{ownId}", new { title = "My notice, edited", body = "Mine." });
            Assert.Equal("My notice, edited", edited.GetProperty("title").GetString());
            using var deleted = await mine.DeleteAsync($"/api/lecturer/modules/YL9701/announcements/{ownId}");
            Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        }
        finally
        {
            using var cleanup = await admin.DeleteAsync($"/api/admin/announcements/{universityId}");
        }
    }
}
