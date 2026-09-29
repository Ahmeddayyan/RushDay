using System.Net;
using System.Net.Http.Json;
using RushDay.Domain.Modules;

namespace RushDay.IntegrationTests.Student;

/// <summary>
/// Student routes never take a student number (02-api.md section 4, D25): identity comes from the session's claims, so
/// a student number in the path or the query string cannot select another student's data.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class StudentIsolationTests(RushDayApiFactory factory)
{
    [Fact]
    public async Task A_student_number_in_the_request_never_selects_another_student()
    {
        using var client = await factory.LoginStudentAsync("S000061");

        foreach (var path in new[] { "/api/me/dashboard?studentNumber=S000062", "/api/me/export.json?studentNumber=S000062" })
        {
            var body = await client.GetJsonAsync(path);
            var number = body.TryGetProperty("student", out var student) ? student.GetProperty("studentNumber") : body.GetProperty("studentNumber");
            Assert.Equal("S000061", number.GetString());
        }

        // There is no route that takes one: the legacy and invented shapes are the /api fallback's 404.
        foreach (var path in new[] { "/api/me/S000062/dashboard", "/api/students/S000062/dashboard", "/api/me/enrolments/S000062" })
        {
            using var response = await client.GetAsync(path);
            await response.AssertProblemAsync(HttpStatusCode.NotFound, "not-found");
        }

        // An extra studentNumber member in the enrolment body is ignored: the enrolment is the caller's.
        await factory.CreateModuleAsync("ZZ1501", Semester.Spring, capacity: 10);
        using (var enrol = await client.PostAsJsonAsync("/api/me/enrolments", new { moduleCode = "ZZ1501", studentNumber = "S000062" }))
        {
            Assert.Equal(HttpStatusCode.Created, enrol.StatusCode);
        }

        Assert.NotNull(await factory.EnrolmentAsync("S000061", "ZZ1501"));
        Assert.Null(await factory.EnrolmentAsync("S000062", "ZZ1501"));
    }
}
