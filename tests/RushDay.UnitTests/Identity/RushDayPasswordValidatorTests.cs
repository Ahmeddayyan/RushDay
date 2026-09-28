using Microsoft.AspNetCore.Identity;
using RushDay.Infrastructure.Identity;

namespace RushDay.UnitTests.Identity;

public sealed class RushDayPasswordValidatorTests
{
    private static readonly RushDayPasswordValidator Validator = new();

    private static Task<IdentityResult> ValidateAsync(string? password, string userName = "S000001")
    {
        // The validator never touches the manager, so no store or DI container is needed.
        var user = new ApplicationUser { UserName = userName, DisplayName = "Test User" };
        return Validator.ValidateAsync(manager: null!, user, password);
    }

    [Theory]
    [InlineData("correct horse battery staple")]
    [InlineData("Student-Demo-2026!")]
    [InlineData("a perfectly ordinary phrase 42")]
    public async Task Accepts_long_passwords_without_the_forbidden_patterns(string password)
    {
        var result = await ValidateAsync(password);

        Assert.True(result.Succeeded);
    }

    [Theory]
    [InlineData("S000001-is-my-login")]
    [InlineData("hello s000001 world")]
    [InlineData("prefixS000001suffix")]
    public async Task Rejects_a_password_containing_the_username_case_insensitively(string password)
    {
        var result = await ValidateAsync(password);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Errors, e => e.Code == RushDayPasswordValidator.ContainsUsernameCode);
    }

    [Theory]
    [InlineData("RushDay-2026-secret")]
    [InlineData("myrushdaypassword")]
    [InlineData("RUSHDAYRUSHDAY")]
    public async Task Rejects_a_password_containing_the_product_name(string password)
    {
        var result = await ValidateAsync(password);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Errors, e => e.Code == RushDayPasswordValidator.ContainsProductNameCode);
    }

    [Theory]
    [InlineData("password1234")]
    [InlineData("PASSWORD1234")]
    [InlineData("123456789012")]
    [InlineData("qwertyuiop123")]
    [InlineData("iloveyou1234")]
    [InlineData("administrator")]
    [InlineData("letmein12345")]
    public async Task Rejects_blocklisted_passwords(string password)
    {
        var result = await ValidateAsync(password);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Errors, e => e.Code == RushDayPasswordValidator.BlockedCode);
    }

    [Fact]
    public async Task Reports_every_failing_rule_at_once()
    {
        var result = await ValidateAsync("S000001-rushday", userName: "S000001");

        Assert.False(result.Succeeded);
        Assert.Equal(2, result.Errors.Count());
        Assert.Contains(result.Errors, e => e.Code == RushDayPasswordValidator.ContainsUsernameCode);
        Assert.Contains(result.Errors, e => e.Code == RushDayPasswordValidator.ContainsProductNameCode);
    }

    [Fact]
    public async Task Every_error_has_a_description_for_the_weak_password_response()
    {
        var result = await ValidateAsync("administrator", userName: "admin");

        Assert.All(result.Errors, e => Assert.False(string.IsNullOrWhiteSpace(e.Description)));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public async Task Leaves_empty_passwords_to_the_length_rule(string? password)
    {
        var result = await ValidateAsync(password);

        Assert.True(result.Succeeded);
    }

    [Fact]
    public async Task Ignores_the_username_rule_when_the_user_has_no_username()
    {
        var user = new ApplicationUser { DisplayName = "No name yet" };

        var result = await Validator.ValidateAsync(manager: null!, user, "a perfectly ordinary phrase 42");

        Assert.True(result.Succeeded);
    }

    [Fact]
    public void Blocklist_holds_at_least_sixty_entries_of_twelve_or_more_characters()
    {
        Assert.True(BlockedPasswords.Set.Count >= 60);
        Assert.All(BlockedPasswords.Set, p => Assert.True(p.Length >= 12, $"'{p}' is shorter than the minimum length and could never be chosen"));
    }
}
