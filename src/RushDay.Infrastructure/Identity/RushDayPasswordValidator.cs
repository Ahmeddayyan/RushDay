using Microsoft.AspNetCore.Identity;

namespace RushDay.Infrastructure.Identity;

/// <summary>
/// The part of the password policy (01-domain-and-data.md section 8) that Identity's options cannot express: no
/// username, no product name, nothing from the blocklist. Length and distinct-character rules stay in
/// <c>IdentityOptions.Password</c> for <see cref="ValidateAsync"/>; <see cref="Check"/> applies the whole policy
/// for callers that have no <c>UserManager</c> (the bootstrap step). Error codes map to 400 <c>weak-password</c>.
/// </summary>
public sealed class RushDayPasswordValidator : IPasswordValidator<ApplicationUser>
{
    public const string TooShortCode = "PasswordTooShort";
    public const string TooLongCode = "PasswordTooLong";
    public const string TooFewUniqueCharsCode = "PasswordRequiresUniqueChars";
    public const string ContainsUsernameCode = "PasswordContainsUsername";
    public const string ContainsProductNameCode = "PasswordContainsProductName";
    public const string BlockedCode = "PasswordBlocked";

    public const int MinimumLength = 12;
    public const int MaximumLength = 128;
    public const int RequiredUniqueChars = 4;

    private const string ProductName = "rushday";

    public Task<IdentityResult> ValidateAsync(UserManager<ApplicationUser> manager, ApplicationUser user, string? password)
    {
        ArgumentNullException.ThrowIfNull(user);

        if (string.IsNullOrEmpty(password))
        {
            // Identity's own validator reports the length failure; nothing to add.
            return Task.FromResult(IdentityResult.Success);
        }

        var errors = new List<IdentityError>(3);
        AddIfFailing(errors, ContainsUsername(user.UserName, password), ContainsUsernameCode);
        AddIfFailing(errors, ContainsProductName(password), ContainsProductNameCode);
        AddIfFailing(errors, IsBlocked(password), BlockedCode);

        return Task.FromResult(errors.Count == 0 ? IdentityResult.Success : IdentityResult.Failed([.. errors]));
    }

    /// <summary>The failing codes of the whole policy (length 12..128, at least 4 distinct characters, the three rules), empty when acceptable.</summary>
    public static IReadOnlyList<string> Check(string username, string password)
    {
        ArgumentNullException.ThrowIfNull(password);

        var codes = new List<string>(6);
        if (password.Length < MinimumLength)
        {
            codes.Add(TooShortCode);
        }

        if (password.Length > MaximumLength)
        {
            codes.Add(TooLongCode);
        }

        if (password.Distinct().Count() < RequiredUniqueChars)
        {
            codes.Add(TooFewUniqueCharsCode);
        }

        if (ContainsUsername(username, password))
        {
            codes.Add(ContainsUsernameCode);
        }

        if (ContainsProductName(password))
        {
            codes.Add(ContainsProductNameCode);
        }

        if (IsBlocked(password))
        {
            codes.Add(BlockedCode);
        }

        return codes;
    }

    private static bool ContainsUsername(string? username, string password) =>
        !string.IsNullOrEmpty(username) && password.Contains(username, StringComparison.OrdinalIgnoreCase);

    private static bool ContainsProductName(string password) =>
        password.Contains(ProductName, StringComparison.OrdinalIgnoreCase);

    private static bool IsBlocked(string password) => BlockedPasswords.Set.Contains(password);

    private static void AddIfFailing(List<IdentityError> errors, bool failing, string code)
    {
        if (failing)
        {
            errors.Add(new IdentityError { Code = code, Description = Describe(code) });
        }
    }

    private static string Describe(string code) => code switch
    {
        ContainsUsernameCode => "The password must not contain your username.",
        ContainsProductNameCode => "The password must not contain the word 'rushday'.",
        BlockedCode => "That password is too common; choose something less guessable.",
        _ => "The password does not meet the policy.",
    };
}
