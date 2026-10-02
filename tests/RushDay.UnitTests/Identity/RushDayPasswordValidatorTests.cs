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
    public void Embedded_blocklist_loads_at_least_a_thousand_entries_of_twelve_or_more_characters()
    {
        Assert.True(BlockedPasswords.Set.Count >= 1_000, $"only {BlockedPasswords.Set.Count} entries loaded");
        Assert.True(BlockedPasswords.Set.Count <= 10_000);
        Assert.All(BlockedPasswords.Set, p => Assert.True(p.Length >= 12, $"'{p}' is shorter than the minimum length and could never be chosen"));
    }

    [Fact]
    public void Embedded_blocklist_keeps_the_hand_written_entries_and_the_seclists_ones()
    {
        // The cast picks one Assert.Contains overload: FrozenSet implements both ISet and IReadOnlySet.
        IReadOnlySet<string> set = BlockedPasswords.Set;
        Assert.Contains("lecturer1234", set);
        Assert.Contains("qwertyqwerty", set);
        Assert.Contains("1q2w3e4r5t6y", set);
    }

    [Theory]
    [InlineData("password1234")]
    [InlineData("PASSWORD1234")]
    [InlineData("QwErTyQwErTy")]
    public void Check_finds_a_blocklisted_entry_case_insensitively(string password)
    {
        var codes = RushDayPasswordValidator.Check("S000001", password);

        Assert.Contains(RushDayPasswordValidator.BlockedCode, codes);
    }

    [Fact]
    public void Check_rejects_eleven_characters()
    {
        var codes = RushDayPasswordValidator.Check("admin", "abcdefghijk");

        Assert.Contains(RushDayPasswordValidator.TooShortCode, codes);
    }

    [Fact]
    public void Check_rejects_one_hundred_and_twenty_nine_characters()
    {
        var password = string.Concat(Enumerable.Range(0, 129).Select(i => (char)('a' + (i % 26))));
        Assert.Equal(129, password.Length);

        var codes = RushDayPasswordValidator.Check("admin", password);

        Assert.Contains(RushDayPasswordValidator.TooLongCode, codes);
    }

    [Fact]
    public void Check_rejects_three_distinct_characters()
    {
        var codes = RushDayPasswordValidator.Check("admin", "abcabcabcabcabc");

        Assert.Contains(RushDayPasswordValidator.TooFewUniqueCharsCode, codes);
    }

    [Fact]
    public void Check_rejects_the_username_and_the_product_name()
    {
        var codes = RushDayPasswordValidator.Check("root", "my ROOT rushday key");

        Assert.Contains(RushDayPasswordValidator.ContainsUsernameCode, codes);
        Assert.Contains(RushDayPasswordValidator.ContainsProductNameCode, codes);
    }

    [Theory]
    [InlineData(RushDayPasswordValidator.MinimumLength)]
    [InlineData(RushDayPasswordValidator.MaximumLength)]
    public void Check_accepts_the_length_boundaries(int length)
    {
        // Four distinct characters, no dictionary word: "abcdefghijkl" itself is on the blocklist.
        var password = string.Concat(Enumerable.Range(0, length).Select(i => "Kq7!"[i % 4]));

        Assert.Empty(RushDayPasswordValidator.Check("admin", password));
    }

    [Fact]
    public void Check_accepts_a_good_passphrase()
    {
        Assert.Empty(RushDayPasswordValidator.Check("admin", "correct horse battery staple"));
    }
}
