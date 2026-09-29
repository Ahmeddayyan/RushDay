using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using RushDay.Domain.Audit;
using RushDay.Domain.Enrolments;
using RushDay.Domain.Grades;
using RushDay.Domain.Modules;
using RushDay.Infrastructure.Identity;
using RushDay.Infrastructure.Seeding;
using RushDay.IntegrationTests.Student;

namespace RushDay.IntegrationTests.Staff;

/// <summary>
/// Helpers for the staff-surface tests. The tests share one database and run one class at a time, so each class
/// works on its own module codes (<c>Y?####</c>), its own students (<c>S000055</c>–<c>S000100</c> and created
/// <c>S9#####</c> numbers), its own academic years for publications, and cleans up anything the student-side tests
/// assert globally: no publication and no pinned university announcement outlives the test that made it, and the
/// settings row is restored.
/// </summary>
public static class StaffData
{
    public const string CurrentYear = StudentData.CurrentYear;
    public const string Reason = "Exam board decision recorded in minutes.";

    public static Task<HttpClient> DemoAdminAsync(this WebApplicationFactory<Program> factory) =>
        factory.LoginAsync(DemoAccounts.AdminUsername, DemoAccounts.AdminPassword);

    public static Task<HttpClient> LecturerAsync(this WebApplicationFactory<Program> factory, string staffNumber) =>
        factory.LoginAsync(staffNumber, DemoAccounts.LecturerPassword);

    /// <summary>
    /// A real (non-demo) administrator with a second factor, signed in: provisioned, signed in (gated), then MFA set
    /// up and enabled, so the session can reach every admin route and act on real accounts.
    /// </summary>
    public static async Task<RealAdmin> RealAdminAsync(this WebApplicationFactory<Program> factory)
    {
        var user = await factory.ProvisionAsync();
        var client = factory.CreateCookieClient();
        var me = await client.LoginAsync(user.UserName!, TestAccounts.Password);
        Assert.True(me.GetProperty("mfaSetupRequired").GetBoolean());

        using var setup = await client.PostAsync("/api/auth/mfa/setup", null);
        Assert.Equal(HttpStatusCode.OK, setup.StatusCode);
        var sharedKey = (await setup.ReadJsonAsync()).GetProperty("sharedKey").GetString()!;

        using var enable = await client.PostAsJsonAsync("/api/auth/mfa/enable", new { code = Totp.FreshCode(sharedKey) });
        var enabled = await enable.ReadJsonAsync();
        Assert.True(enable.StatusCode == HttpStatusCode.OK, $"MFA enable answered {(int)enable.StatusCode}: {enabled}");
        client.UseCsrf(enabled.GetProperty("csrfToken").GetString()!);
        return new RealAdmin(client, user, sharedKey);
    }

    public static async Task<JsonElement> SendJsonAsync(this HttpClient client, HttpMethod method, string path, object? body, HttpStatusCode expected)
    {
        ArgumentNullException.ThrowIfNull(client);
        using var request = new HttpRequestMessage(method, path);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        using var response = await client.SendAsync(request);
        var json = await response.ReadJsonAsync();
        Assert.True(response.StatusCode == expected, $"{method} {path} answered {(int)response.StatusCode}, expected {(int)expected}: {json}");
        return json;
    }

    public static Task<JsonElement> PostJsonAsync(this HttpClient client, string path, object? body, HttpStatusCode expected = HttpStatusCode.OK) =>
        client.SendJsonAsync(HttpMethod.Post, path, body, expected);

    public static Task<JsonElement> PutJsonAsync(this HttpClient client, string path, object? body, HttpStatusCode expected = HttpStatusCode.OK) =>
        client.SendJsonAsync(HttpMethod.Put, path, body, expected);

    /// <summary>Creates a module through the administrator's route (so <c>catalogue:all</c> is invalidated as in production).</summary>
    public static Task<JsonElement> CreateModuleAsync(this HttpClient admin, string code, Semester semester = Semester.Autumn, int capacity = 100, int credits = 15) =>
        admin.PostJsonAsync("/api/admin/modules", new { code, title = "Staff test " + code, description = "Created by a staff test.", credits, capacity, semester = semester.ToString().ToLowerInvariant() }, HttpStatusCode.Created);

    /// <summary>Assigns a leader (and optionally a teacher) through the administrator's route.</summary>
    public static Task<JsonElement> AssignAsync(this HttpClient admin, string code, string leader, string? teacher = null)
    {
        var assignments = new List<object> { new { staffNumber = leader, role = "leader" } };
        if (teacher is not null)
        {
            assignments.Add(new { staffNumber = teacher, role = "teacher" });
        }

        return admin.PutJsonAsync($"/api/admin/modules/{code}/lecturers", new { assignments });
    }

    /// <summary>The administrator's override enrolment (current year; windows and credits ignored).</summary>
    public static Task<JsonElement> OverrideEnrolAsync(this HttpClient admin, string studentNumber, string code, bool forceCapacity = false, HttpStatusCode expected = HttpStatusCode.Created) =>
        admin.PostJsonAsync($"/api/admin/students/{studentNumber}/enrolments", new { moduleCode = code, reason = Reason, forceCapacity }, expected);

    public static Task<JsonElement> SaveMarksAsync(this HttpClient lecturer, string code, object[] rows, HttpStatusCode expected = HttpStatusCode.OK) =>
        lecturer.PutJsonAsync($"/api/lecturer/modules/{code}/marks", new { rows }, expected);

    /// <summary>
    /// A module of <paramref name="academicYear"/> whose grades are written straight to the table: one active
    /// enrolment per student (the year need not be the current one, so a publication test owns its whole
    /// (year, semester)), and a grade per entry of <paramref name="marks"/> (null: no grade row).
    /// </summary>
    public static async Task SeedModuleAsync(
        this WebApplicationFactory<Program> factory,
        string code,
        Semester semester,
        string academicYear,
        IReadOnlyList<(string StudentNumber, int? Mark)> marks,
        GradeStatus status = GradeStatus.Submitted)
    {
        await factory.CreateModuleAsync(code, semester, capacity: 100);
        foreach (var (studentNumber, mark) in marks)
        {
            await factory.InsertEnrolmentAsync(studentNumber, code, academicYear);
            if (mark is { } value)
            {
                await factory.PutGradeAsync(studentNumber, code, status, value);
            }
        }
    }

    public static Task<Grade> GradeAsync(this WebApplicationFactory<Program> factory, string studentNumber, string code) =>
        factory.WithDbAsync(async db =>
        {
            var studentId = await db.Students.Where(s => s.StudentNumber == studentNumber).Select(s => s.Id).SingleAsync();
            var moduleId = await db.Modules.Where(m => m.Code == code).Select(m => m.Id).SingleAsync();
            return await db.Grades.AsNoTracking().SingleAsync(g => g.StudentId == studentId && g.ModuleId == moduleId);
        });

    public static Task SetGradeStatusAsync(this WebApplicationFactory<Program> factory, string code, GradeStatus status) =>
        factory.WithDbAsync(async db =>
        {
            var moduleId = await db.Modules.Where(m => m.Code == code).Select(m => m.Id).SingleAsync();
            await db.Grades.Where(g => g.ModuleId == moduleId).ExecuteUpdateAsync(s => s.SetProperty(g => g.Status, status));
        });

    /// <summary>Audit rows of an action, newest first, optionally only those about one subject.</summary>
    public static Task<List<AuditEvent>> AuditAsync(this WebApplicationFactory<Program> factory, string action, string? subjectId = null) =>
        factory.WithDbAsync(db =>
        {
            var rows = db.AuditEvents.AsNoTracking().Where(a => a.Action == action);
            if (subjectId is not null)
            {
                rows = rows.Where(a => a.SubjectId == subjectId);
            }

            return rows.OrderByDescending(a => a.OccurredAt).ThenByDescending(a => a.Id).ToListAsync();
        });

    public static JsonElement DetailsOf(AuditEvent row)
    {
        ArgumentNullException.ThrowIfNull(row);
        return JsonDocument.Parse(row.Details!).RootElement.Clone();
    }

    public static Task<ApplicationUser?> UserOfStudentAsync(this WebApplicationFactory<Program> factory, string studentNumber) =>
        factory.WithDbAsync(async db =>
        {
            var studentId = await db.Students.Where(s => s.StudentNumber == studentNumber).Select(s => s.Id).SingleAsync();
            return await db.Users.AsNoTracking().SingleOrDefaultAsync(u => u.StudentId == studentId);
        });

    public static Task<int> ActiveCountAsync(this WebApplicationFactory<Program> factory, string code) =>
        factory.WithDbAsync(async db =>
        {
            var moduleId = await db.Modules.Where(m => m.Code == code).Select(m => m.Id).SingleAsync();
            return await db.Enrolments.CountAsync(e => e.ModuleId == moduleId && e.Status == EnrolmentStatus.Active && e.AcademicYear == CurrentYear);
        });

    /// <summary>The student's own view of one module's visible grade on <c>/api/me/results</c>, or null when not visible.</summary>
    public static async Task<JsonElement?> VisibleResultAsync(this HttpClient student, string code)
    {
        var results = await student.GetJsonAsync("/api/me/results");
        foreach (var semester in results.GetProperty("semesters").EnumerateArray())
        {
            foreach (var result in semester.GetProperty("results").EnumerateArray())
            {
                if (result.GetProperty("moduleCode").GetString() == code)
                {
                    return result;
                }
            }
        }

        return null;
    }

    /// <summary>Unpublishes or cancels every publication of a test's own academic year, so none outlives the test.</summary>
    public static async Task RemovePublicationsAsync(this WebApplicationFactory<Program> factory, HttpClient admin, string academicYear)
    {
        ArgumentNullException.ThrowIfNull(admin);
        var now = factory is RushDayApiFactory api ? api.Clock.GetUtcNow() : DateTimeOffset.UtcNow;
        var publications = await factory.WithDbAsync(db => db.ResultsPublications.AsNoTracking().Where(p => p.AcademicYear == academicYear).ToListAsync());
        foreach (var publication in publications)
        {
            if (publication.PublishAt > now)
            {
                using var cancel = await admin.DeleteAsync($"/api/admin/results/publications/{publication.Id}");
                Assert.Equal(HttpStatusCode.OK, cancel.StatusCode);
            }
            else
            {
                using var unpublish = await admin.PostAsJsonAsync($"/api/admin/results/publications/{publication.Id}/unpublish", new { reason = Reason });
                Assert.Equal(HttpStatusCode.OK, unpublish.StatusCode);
            }
        }
    }
}

/// <summary>A real administrator's session with the shared key of its second factor.</summary>
public sealed record RealAdmin(HttpClient Client, ApplicationUser User, string SharedKey) : IDisposable
{
    public void Dispose() => Client.Dispose();
}
