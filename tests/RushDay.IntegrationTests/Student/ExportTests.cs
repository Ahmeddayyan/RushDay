using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RushDay.Domain.Audit;
using RushDay.Domain.Grades;
using RushDay.Domain.Modules;
using RushDay.Infrastructure.Seeding;

namespace RushDay.IntegrationTests.Student;

/// <summary>
/// <c>GET /api/me/export.json</c> (02-api.md section 8.3): <c>StudentExport</c> with visible grades only (no drafts),
/// served as an attachment, and audited as <c>student.exported_self</c> (03-security.md section 7).
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class ExportTests(RushDayApiFactory factory)
{
    /// <summary>
    /// <c>GradeResult</c> (02-api.md section 7): what the student sees. No <c>status</c>, <c>version</c> or
    /// <c>visibleToStudent</c>: they describe states the student never sees (review S4 D4, T6).
    /// </summary>
    private static readonly string[] GradeProperties =
    [
        "moduleCode", "moduleTitle", "credits", "semester", "academicYear", "outcome", "mark", "publishedAt", "correctedAt",
    ];

    private static readonly string[] EnrolmentProperties =
    [
        "moduleCode", "title", "credits", "semester", "academicYear", "status", "source", "enrolledAt", "withdrawnAt",
    ];

    [Fact]
    public async Task Export_has_the_shape_without_drafts_and_is_audited()
    {
        const string student = "S000081";
        await factory.CreateModuleAsync("ZZ1301", Semester.Spring, capacity: 10);
        using var client = await factory.LoginStudentAsync(student);
        using (var enrol = await client.EnrolAsync("ZZ1301"))
        {
            Assert.Equal(HttpStatusCode.Created, enrol.StatusCode);
        }

        await factory.PutGradeAsync(student, "ZZ1301", GradeStatus.Draft, 33);

        using var response = await client.GetAsync("/api/me/export.json");
        var body = await response.ReadJsonAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("attachment; filename=\"rushday-S000081.json\"", response.Content.Headers.ContentDisposition?.ToString());
        Assert.Equal(["classification", "enrolments", "exportedAt", "grades", "student", "weightedAverage"], body.EnumerateObject().Select(p => p.Name).Order());

        var profile = body.GetProperty("student");
        Assert.Equal(["email", "fullName", "leftAt", "programme", "studentNumber", "yearOfStudy"], profile.EnumerateObject().Select(p => p.Name).Order());
        Assert.Equal(student, profile.GetProperty("studentNumber").GetString());
        Assert.Equal(JsonValueKind.Null, profile.GetProperty("leftAt").ValueKind);

        var enrolments = body.GetProperty("enrolments").EnumerateArray().ToList();
        Assert.Equal(5, enrolments.Count);
        Assert.All(enrolments, e => Assert.Equal(EnrolmentProperties.Order(), e.EnumerateObject().Select(p => p.Name).Order()));
        var own = enrolments.Single(e => e.GetProperty("moduleCode").GetString() == "ZZ1301");
        Assert.Equal(("active", "self", StudentData.CurrentYear), (own.GetProperty("status").GetString(), own.GetProperty("source").GetString(), own.GetProperty("academicYear").GetString()));
        Assert.Equal(4, enrolments.Count(e => e.GetProperty("source").GetString() == "seed"));

        // Visible grades only: the four published 2025/26 marks; the draft on ZZ1301 is not there.
        var grades = body.GetProperty("grades").EnumerateArray().ToList();
        Assert.Equal(4, grades.Count);
        Assert.All(grades, g =>
        {
            Assert.Equal(GradeProperties.Order(), g.EnumerateObject().Select(p => p.Name).Order());
            Assert.Equal(StudentData.PreviousYear, g.GetProperty("academicYear").GetString());
            Assert.NotEqual(JsonValueKind.Null, g.GetProperty("publishedAt").ValueKind);
        });
        Assert.DoesNotContain(grades, g => g.GetProperty("moduleCode").GetString() == "ZZ1301");
        Assert.True(body.GetProperty("weightedAverage").GetDouble() > 0);
        Assert.StartsWith("2026-09-27T", body.GetProperty("exportedAt").GetString(), StringComparison.Ordinal);

        // The read of personal data is itself audited, naming the student and the actor.
        var audit = await factory.WithDbAsync(db => db.AuditEvents.AsNoTracking()
            .Where(a => a.Action == AuditActions.StudentExportedSelf && a.SubjectId == student)
            .ToListAsync());
        var row = Assert.Single(audit);
        Assert.Equal(AuditSubjects.Student, row.SubjectType);
        Assert.Equal(student, row.ActorUsername);
        Assert.Equal("Student", row.ActorRole);
        Assert.Equal(await factory.StudentIdAsync(student), row.StudentId);
        Assert.Contains("\"studentNumber\": \"S000081\"", row.Details, StringComparison.Ordinal);
        Assert.False(string.IsNullOrEmpty(row.RequestId));
        Assert.False(string.IsNullOrEmpty(row.IpHash));
    }

    [Fact]
    public async Task Export_is_for_students_only()
    {
        using var admin = await factory.LoginAsync(DemoAccounts.AdminUsername, DemoAccounts.AdminPassword);
        using var response = await admin.GetAsync("/api/me/export.json");
        await response.AssertProblemAsync(HttpStatusCode.Forbidden, "forbidden");
    }
}
