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

    [Fact]
    public void Catalogue_is_not_empty()
    {
        Assert.NotEmpty(Actions());
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
        string[] expected =
        [
            "auth.locked_out", "auth.password_changed",
            "enrolment.created", "enrolment.withdrawn", "enrolment.admin_created", "enrolment.admin_withdrawn",
            "grade.entered", "grade.changed",
            "module.marks_submitted", "module.returned_to_draft", "module.created", "module.updated", "module.lecturers_set",
            "results.published", "results.rescheduled",
            "announcement.created", "announcement.updated", "announcement.deleted",
            "account.provisioned", "account.locked", "account.unlocked", "account.disabled", "account.enabled", "account.password_reset",
            "settings.changed",
            "window.created", "window.updated", "window.deleted",
            "student.created", "lecturer.created",
            "ops.reconciled",
        ];

        Assert.Equal(expected.Order(StringComparer.Ordinal), Actions().Order(StringComparer.Ordinal));
    }

    [GeneratedRegex("^[a-z]+\\.[a-z_]+$")]
    private static partial Regex ActionPattern();
}
