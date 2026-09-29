using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using RushDay.Domain.Audit;
using RushDay.Infrastructure.Seeding;
using RushDay.IntegrationTests.Student;

namespace RushDay.IntegrationTests.Staff;

/// <summary>
/// Accounts through the real routes and <c>AccountService</c> (02-api.md section 8.5): provision with a temporary
/// password shown once and a forced change at first sign-in; lock, unlock, disable, enable, reset password and reset
/// the second factor, each audited and each rotating the stamp where it must; no locking or disabling oneself; demo
/// accounts are read-only and a demo actor may not touch a real account. Lecturers <c>L91###</c> and students
/// <c>S97####</c> are created here.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class AccountTests(RushDayApiFactory factory)
{
    [Fact]
    public async Task Provisioned_accounts_must_change_their_password_first()
    {
        using var real = await factory.RealAdminAsync();
        var admin = real.Client;
        await admin.PostJsonAsync("/api/admin/lecturers", new { staffNumber = "L91001", fullName = "Tamsin Rowe", title = "Dr", department = "CS" }, HttpStatusCode.Created);

        var provisioned = await admin.PostJsonAsync("/api/admin/accounts", new { username = "trowe", displayName = "Tamsin Rowe", role = "lecturer", staffNumber = "l91001" }, HttpStatusCode.Created);
        var temporary = provisioned.GetProperty("temporaryPassword").GetString()!;
        Assert.Equal(16, temporary.Length);
        Assert.Matches("^[A-HJ-NP-Za-km-z2-9]{16}$", temporary);
        var account = provisioned.GetProperty("account");
        Assert.Equal(("trowe", "Lecturer", "L91001", "active"), (account.GetProperty("username").GetString(), account.GetProperty("role").GetString(), account.GetProperty("staffNumber").GetString(), account.GetProperty("state").GetString()));
        Assert.True(account.GetProperty("mustChangePassword").GetBoolean());
        Assert.False(account.GetProperty("isDemo").GetBoolean());
        var audit = Assert.Single(await factory.AuditAsync(AuditActions.AccountProvisioned, account.GetProperty("id").GetString()));
        Assert.Equal(("trowe", "Lecturer", "L91001"), (StaffData.DetailsOf(audit).GetProperty("username").GetString(), StaffData.DetailsOf(audit).GetProperty("role").GetString(), StaffData.DetailsOf(audit).GetProperty("staffNumber").GetString()));
        Assert.DoesNotContain(temporary, audit.Details!, StringComparison.Ordinal);

        // First sign-in: every staff route answers password-change-required until the password is changed.
        using var lecturer = factory.CreateCookieClient();
        var me = await lecturer.LoginAsync("trowe", temporary);
        Assert.True(me.GetProperty("mustChangePassword").GetBoolean());
        using (var gated = await lecturer.GetAsync("/api/lecturer/modules"))
        {
            await gated.AssertProblemAsync(HttpStatusCode.Forbidden, "password-change-required");
        }

        using (var change = await lecturer.PostAsJsonAsync("/api/auth/change-password", new { currentPassword = temporary, newPassword = TestAccounts.OtherPassword }))
        {
            Assert.Equal(HttpStatusCode.NoContent, change.StatusCode);
        }

        var refreshed = await lecturer.GetJsonAsync("/api/auth/me");
        lecturer.UseCsrf(refreshed.GetProperty("csrfToken").GetString()!);
        Assert.Empty((await lecturer.GetJsonAsync("/api/lecturer/modules")).EnumerateArray());

        // The rules of provisioning.
        var taken = await admin.PostJsonAsync("/api/admin/accounts", new { username = "TROWE", displayName = "x", role = "Admin" }, HttpStatusCode.Conflict);
        Assert.Equal("urn:rushday:username-taken", taken.GetProperty("type").GetString());
        var hasAccount = await admin.PostJsonAsync("/api/admin/accounts", new { username = "trowe2", displayName = "x", role = "Lecturer", staffNumber = "L91001" }, HttpStatusCode.Conflict);
        Assert.Equal("urn:rushday:principal-has-account", hasAccount.GetProperty("type").GetString());
        var mismatch = await admin.PostJsonAsync("/api/admin/accounts", new { username = "mismatch1", displayName = "x", role = "Student", staffNumber = "L91001" }, HttpStatusCode.UnprocessableEntity);
        Assert.Equal("urn:rushday:role-principal-mismatch", mismatch.GetProperty("type").GetString());
        var noStudent = await admin.PostJsonAsync("/api/admin/accounts", new { username = "nostudent1", displayName = "x", role = "Student", studentNumber = "S979999" }, HttpStatusCode.NotFound);
        Assert.Equal("urn:rushday:student-not-found", noStudent.GetProperty("type").GetString());
        var weak = await admin.PostJsonAsync("/api/admin/accounts", new { username = "weakadmin1", displayName = "x", role = "Admin", temporaryPassword = "password1234" }, HttpStatusCode.BadRequest);
        Assert.Equal("urn:rushday:weak-password", weak.GetProperty("type").GetString());
        Assert.True(weak.GetProperty("errors").TryGetProperty("temporaryPassword", out _));
        foreach (var invalid in new object[]
        {
            new { username = "bad name", displayName = "x", role = "Admin" },
            new { username = "okname", displayName = "x", role = "Registrar" },
        })
        {
            using var response = await admin.PostAsJsonAsync("/api/admin/accounts", invalid);
            await response.AssertProblemAsync(HttpStatusCode.BadRequest, "validation");
        }
    }

    [Fact]
    public async Task Lock_unlock_disable_enable_reset_password_and_reset_mfa()
    {
        using var real = await factory.RealAdminAsync();
        var admin = real.Client;
        var target = await factory.ProvisionAsync();
        var id = target.Id;

        var locked = await admin.PostJsonAsync($"/api/admin/accounts/{id}/lock", null);
        Assert.Equal(("locked", "9999-12-31T00:00:00.000Z"), (locked.GetProperty("state").GetString(), locked.GetProperty("lockoutEnd").GetString()));
        Assert.NotEqual(target.SecurityStamp, (await factory.ReadUserAsync(id)).SecurityStamp);
        using (var client = factory.CreateCookieClient())
        {
            await client.RefreshCsrfAsync();
            using var login = await client.PostLoginAsync(target.UserName!, TestAccounts.Password);
            await login.AssertProblemAsync(HttpStatusCode.Unauthorized, "invalid-credentials");
        }

        Assert.Equal("active", (await admin.PostJsonAsync($"/api/admin/accounts/{id}/unlock", null)).GetProperty("state").GetString());
        Assert.Equal("disabled", (await admin.PostJsonAsync($"/api/admin/accounts/{id}/disable", null)).GetProperty("state").GetString());
        Assert.Equal("active", (await admin.PostJsonAsync($"/api/admin/accounts/{id}/enable", null)).GetProperty("state").GetString());

        var reset = await admin.PostJsonAsync($"/api/admin/accounts/{id}/reset-password", null);
        var temporary = reset.GetProperty("temporaryPassword").GetString()!;
        Assert.Equal(16, temporary.Length);
        Assert.True((await factory.ReadUserAsync(id)).MustChangePassword);
        var chosen = await admin.PostJsonAsync($"/api/admin/accounts/{id}/reset-password", new { temporaryPassword = TestAccounts.OtherPassword });
        Assert.Equal(TestAccounts.OtherPassword, chosen.GetProperty("temporaryPassword").GetString());

        var mfa = await admin.PostJsonAsync($"/api/admin/accounts/{id}/reset-mfa", null);
        Assert.False(mfa.GetProperty("mfaEnabled").GetBoolean());

        foreach (var action in new[] { AuditActions.AccountLocked, AuditActions.AccountUnlocked, AuditActions.AccountDisabled, AuditActions.AccountEnabled, AuditActions.AccountMfaReset })
        {
            var row = Assert.Single(await factory.AuditAsync(action, id.ToString()));
            Assert.Equal((real.User.UserName, target.UserName), (row.ActorUsername, StaffData.DetailsOf(row).GetProperty("username").GetString()));
        }

        Assert.Equal(2, (await factory.AuditAsync(AuditActions.AccountPasswordReset, id.ToString())).Count);

        // The list filters by state and role and searches by username prefix.
        var listed = await admin.GetJsonAsync($"/api/admin/accounts?q={target.UserName![..6]}&role=admin&state=active");
        Assert.Contains(target.UserName, listed.GetProperty("items").Strings("username"));
        using (var badState = await admin.GetAsync("/api/admin/accounts?state=frozen"))
        {
            await badState.AssertProblemAsync(HttpStatusCode.BadRequest, "validation");
        }

        using var missing = await admin.PostAsync($"/api/admin/accounts/{Guid.NewGuid()}/lock", null);
        await missing.AssertProblemAsync(HttpStatusCode.NotFound, "account-not-found");
    }

    [Fact]
    public async Task Administrators_cannot_lock_or_disable_themselves_but_may_reset_their_password()
    {
        using var real = await factory.RealAdminAsync();
        var admin = real.Client;
        var self = real.User.Id;

        var lockSelf = await admin.PostJsonAsync($"/api/admin/accounts/{self}/lock", null, HttpStatusCode.UnprocessableEntity);
        Assert.Equal("urn:rushday:self-lockout", lockSelf.GetProperty("type").GetString());
        var disableSelf = await admin.PostJsonAsync($"/api/admin/accounts/{self}/disable", null, HttpStatusCode.UnprocessableEntity);
        Assert.Equal("urn:rushday:self-lockout", disableSelf.GetProperty("type").GetString());
        Assert.Null((await factory.ReadUserAsync(self)).LockoutEnd);

        var reset = await admin.PostJsonAsync($"/api/admin/accounts/{self}/reset-password", null);
        Assert.False(string.IsNullOrEmpty(reset.GetProperty("temporaryPassword").GetString()));
    }

    [Fact]
    public async Task Demo_showcase_accounts_cannot_be_altered()
    {
        using var real = await factory.RealAdminAsync();
        var student = await factory.ReadUserAsync(DemoAccounts.StudentUsername);
        var demoAdmin = await factory.ReadUserAsync(DemoAccounts.AdminUsername);

        foreach (var action in new[] { "lock", "unlock", "disable", "enable", "reset-password", "reset-mfa" })
        {
            foreach (var target in new[] { student.Id, demoAdmin.Id })
            {
                var refused = await real.Client.PostJsonAsync($"/api/admin/accounts/{target}/{action}", null, HttpStatusCode.Conflict);
                Assert.Equal(("urn:rushday:demo-account", "Demo accounts are read-only."), (refused.GetProperty("type").GetString(), refused.GetProperty("detail").GetString()));
            }
        }

        var after = await factory.ReadUserAsync(DemoAccounts.StudentUsername);
        Assert.Equal((student.SecurityStamp, (DateTimeOffset?)null, (DateTimeOffset?)null), (after.SecurityStamp, after.LockoutEnd, after.DisabledAt));

        // The demo administrator, whose password is public, cannot touch a real account either...
        using var demo = await factory.DemoAdminAsync();
        var refusedReal = await demo.PostJsonAsync($"/api/admin/accounts/{real.User.Id}/disable", null, HttpStatusCode.Conflict);
        Assert.Equal("urn:rushday:demo-account", refusedReal.GetProperty("type").GetString());
        Assert.Null((await factory.ReadUserAsync(real.User.Id)).DisabledAt);

        // ...and what it provisions is a demo account, never forced to change its password.
        await demo.PostJsonAsync("/api/admin/students", new { studentNumber = "S970001", fullName = "Demo Made", programme = "BSc", yearOfStudy = 1 }, HttpStatusCode.Created);
        var provisioned = await demo.PostJsonAsync("/api/admin/accounts", new { username = "S970001", displayName = "Demo Made", role = "Student", studentNumber = "S970001" }, HttpStatusCode.Created);
        Assert.True(provisioned.GetProperty("account").GetProperty("isDemo").GetBoolean());
        Assert.False(provisioned.GetProperty("account").GetProperty("mustChangePassword").GetBoolean());
        Assert.Equal(JsonValueKind.String, provisioned.GetProperty("temporaryPassword").ValueKind);
    }
}
