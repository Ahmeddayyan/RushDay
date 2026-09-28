using Microsoft.AspNetCore.Identity;

namespace RushDay.Infrastructure.Identity;

/// <summary>
/// The part of the password policy (00-overview.md D22) that Identity's options cannot express: no username,
/// no product name, nothing from the blocklist. Length and distinct-character rules stay in
/// <c>IdentityOptions.Password</c>. Error codes map to 400 <c>weak-password</c>.
/// </summary>
public sealed class RushDayPasswordValidator : IPasswordValidator<ApplicationUser>
{
    public const string ContainsUsernameCode = "PasswordContainsUsername";
    public const string ContainsProductNameCode = "PasswordContainsProductName";
    public const string BlockedCode = "PasswordBlocked";

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

        if (!string.IsNullOrEmpty(user.UserName)
            && password.Contains(user.UserName, StringComparison.OrdinalIgnoreCase))
        {
            errors.Add(new IdentityError
            {
                Code = ContainsUsernameCode,
                Description = "The password must not contain your username.",
            });
        }

        if (password.Contains(ProductName, StringComparison.OrdinalIgnoreCase))
        {
            errors.Add(new IdentityError
            {
                Code = ContainsProductNameCode,
                Description = "The password must not contain the word 'rushday'.",
            });
        }

        if (BlockedPasswords.Set.Contains(password))
        {
            errors.Add(new IdentityError
            {
                Code = BlockedCode,
                Description = "That password is too common; choose something less guessable.",
            });
        }

        return Task.FromResult(errors.Count == 0 ? IdentityResult.Success : IdentityResult.Failed([.. errors]));
    }
}
