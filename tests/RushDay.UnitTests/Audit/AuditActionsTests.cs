using System.Reflection;
using System.Text.RegularExpressions;
using RushDay.Domain.Audit;

namespace RushDay.UnitTests.Audit;

public sealed partial class AuditActionsTests
{
    private static IReadOnlyList<string> Actions() =>
        typeof(AuditActions)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.IsLiteral && f.FieldType == typeof(string))
            .Select(f => (string)f.GetRawConstantValue()!)
            .ToList();

    /// <summary>The 49 actions of 03-security.md section 7, verbatim.</summary>
    private static readonly string[] Catalogue =
    [
        "auth.locked_out", "auth.password_changed",
        "account.mfa_setup_started", "account.mfa_enabled",
        "enrolment.created", "enrolment.withdrawn", "enrolment.admin_created", "enrolment.admin_withdrawn",
        "grade.entered", "grade.changed", "grade.corrected",
        "module.marks_submitted", "module.returned_to_draft",
        "results.published", "results.rescheduled", "results.cancelled", "results.unpublished",
        "announcement.created", "announcement.updated", "announcement.deleted",
        "account.provisioned",
        "account.locked", "account.unlocked", "account.disabled", "account.enabled", "account.password_reset", "account.mfa_reset",
        "settings.changed",
        "window.created", "window.updated", "window.deleted",
        "module.created", "module.updated",
        "module.lecturers_set",
        "module.trimmed",
        "student.created", "student.updated", "student.left",
        "student.viewed",
        "student.exported", "student.exported_self",
        "lecturer.created", "lecturer.updated", "lecturer.left",
        "roster.exported",
        "audit.exported",
        "ops.reconciled",
        "system.demo_reset",
        "system.demo_accounts_disabled",
    ];

    [Fact]
    public void Catalogue_has_forty_nine_actions()
    {
        Assert.Equal(49, Catalogue.Length);
        Assert.Equal(49, Actions().Count);
    }

    [Fact]
    public void Every_action_is_unique()
    {
        var actions = Actions();

        Assert.Equal(actions.Count, actions.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void Every_action_is_a_lowercase_dotted_identifier_that_fits_the_column()
    {
        Assert.All(Actions(), action =>
        {
            Assert.Matches(ActionPattern(), action);
            Assert.True(action.Length <= 64, $"'{action}' exceeds varchar(64)");
        });
    }

    [Fact]
    public void Catalogue_matches_the_security_specification()
    {
        Assert.Equal(Catalogue.Order(StringComparer.Ordinal), Actions().Order(StringComparer.Ordinal));
    }

    [Fact]
    public void Every_action_maps_to_a_listed_subject()
    {
        Assert.All(Actions(), action => Assert.Contains(AuditActions.SubjectOf(action), AuditSubjects.All));
    }

    [Theory]
    [InlineData("auth.locked_out", AuditSubjects.Account)]
    [InlineData("account.provisioned", AuditSubjects.Account)]
    [InlineData("enrolment.created", AuditSubjects.Enrolment)]
    [InlineData("grade.corrected", AuditSubjects.Grade)]
    [InlineData("module.trimmed", AuditSubjects.Module)]
    [InlineData("results.unpublished", AuditSubjects.Publication)]
    [InlineData("announcement.deleted", AuditSubjects.Announcement)]
    [InlineData("settings.changed", AuditSubjects.Settings)]
    [InlineData("window.updated", AuditSubjects.Window)]
    [InlineData("student.exported_self", AuditSubjects.Student)]
    [InlineData("lecturer.left", AuditSubjects.Lecturer)]
    [InlineData("roster.exported", AuditSubjects.Module)]
    [InlineData("audit.exported", AuditSubjects.System)]
    [InlineData("ops.reconciled", AuditSubjects.System)]
    [InlineData("system.demo_accounts_disabled", AuditSubjects.System)]
    public void Subject_of_follows_the_prefix_table(string action, string expected)
    {
        Assert.Equal(expected, AuditActions.SubjectOf(action));
    }

    [Theory]
    [InlineData("nothing.here")]
    [InlineData("grade")]
    public void Subject_of_throws_for_an_unknown_action(string action)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => AuditActions.SubjectOf(action));
    }

    [Fact]
    public void Subjects_are_unique_and_fit_the_column()
    {
        Assert.Equal(11, AuditSubjects.All.Count);
        Assert.Equal(AuditSubjects.All.Count, AuditSubjects.All.Distinct(StringComparer.Ordinal).Count());
        Assert.All(AuditSubjects.All, subject => Assert.True(subject.Length <= 32));
    }

    [GeneratedRegex("^[a-z]+\\.[a-z_]+$")]
    private static partial Regex ActionPattern();
}
