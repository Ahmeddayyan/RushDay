using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using RushDay.Api.Contracts;
using RushDay.Api.Security;
using RushDay.Infrastructure.Accounts;
using RushDay.Infrastructure.Queries;

namespace RushDay.Api.Endpoints;

/// <summary>
/// Accounts, <c>/api/admin/accounts</c> (02-api.md section 8.5), all through <see cref="AccountService"/>: provision
/// (a temporary password shown once), lock, unlock, disable, enable, reset password, reset the second factor. Every
/// mutation of a demo account, and every mutation of a real account by a demo actor, is 409 <c>demo-account</c>;
/// what a demo actor provisions is itself a demo account. Lock and disable of one's own account are
/// <c>self-lockout</c>; resetting one's own password is allowed (the session ends at its next stamp check).
/// </summary>
public static class AdminAccountEndpoints
{
    public const int DefaultPageSize = 25;
    public const int MaxPageSize = 100;

    public static RouteGroupBuilder MapAdminAccountEndpoints(this RouteGroupBuilder admin)
    {
        ArgumentNullException.ThrowIfNull(admin);

        admin.MapGet("/accounts", ListAsync).WithName("AdminAccounts");
        admin.MapPost("/accounts", ProvisionAsync).RequireRateLimiting(RateLimitPolicies.Write).WithName("ProvisionAccount");

        var account = admin.MapGroup("/accounts/{id:guid}");
        account.MapPost("/lock", (Guid id, AccountService accounts, AdminAccountQuery query, TimeProvider clock, CancellationToken ct) =>
            MutateAsync(id, accounts.LockAsync, query, clock, ct)).RequireRateLimiting(RateLimitPolicies.Write).WithName("LockAccount");
        account.MapPost("/unlock", (Guid id, AccountService accounts, AdminAccountQuery query, TimeProvider clock, CancellationToken ct) =>
            MutateAsync(id, accounts.UnlockAsync, query, clock, ct)).RequireRateLimiting(RateLimitPolicies.Write).WithName("UnlockAccount");
        account.MapPost("/disable", (Guid id, AccountService accounts, AdminAccountQuery query, TimeProvider clock, CancellationToken ct) =>
            MutateAsync(id, accounts.DisableAsync, query, clock, ct)).RequireRateLimiting(RateLimitPolicies.Write).WithName("DisableAccount");
        account.MapPost("/enable", (Guid id, AccountService accounts, AdminAccountQuery query, TimeProvider clock, CancellationToken ct) =>
            MutateAsync(id, accounts.EnableAsync, query, clock, ct)).RequireRateLimiting(RateLimitPolicies.Write).WithName("EnableAccount");
        account.MapPost("/reset-mfa", (Guid id, AccountService accounts, AdminAccountQuery query, TimeProvider clock, CancellationToken ct) =>
            MutateAsync(id, accounts.ResetMfaAsync, query, clock, ct)).RequireRateLimiting(RateLimitPolicies.Write).WithName("ResetAccountMfa");
        account.MapPost("/reset-password", ResetPasswordAsync)
            .RequireRateLimiting(RateLimitPolicies.Write)
            .WithName("ResetAccountPassword");
        return admin;
    }

    private static async Task<IResult> ListAsync([AsParameters] AccountListParameters parameters, AdminAccountQuery query, TimeProvider clock, CancellationToken cancellationToken)
    {
        var page = PageRequest.Of(parameters.Page, parameters.PageSize, DefaultPageSize, MaxPageSize);
        if (page.IsTooDeep)
        {
            return StaffPatterns.PageTooDeep();
        }

        var rows = await query.ListAsync(parameters.Q, RoleNameAttribute.Canonical(parameters.Role), parameters.ParsedState(), page, clock.GetUtcNow(), cancellationToken);
        return TypedResults.Ok(new Paged<AccountView>([.. rows.Items.Select(AccountView.From)], rows.Page, rows.PageSize, rows.Total));
    }

    private static async Task<IResult> ProvisionAsync(ProvisionAccountBody request, AccountService accounts, AdminAccountQuery query, TimeProvider clock, CancellationToken cancellationToken)
    {
        var result = await accounts.ProvisionAsync(
            new ProvisionAccountRequest(
                request.Username!,
                request.DisplayName!.Trim(),
                RoleNameAttribute.Canonical(request.Role)!,
                request.StudentNumber,
                request.StaffNumber,
                request.Email,
                request.TemporaryPassword),
            cancellationToken);
        if (!result.Succeeded)
        {
            return Problem(result.Error, result.Codes, "temporaryPassword");
        }

        var view = await query.FindAsync(result.Value!.User.Id, clock.GetUtcNow(), cancellationToken);
        return TypedResults.Created((string?)null, new ProvisionAccountResponse(AccountView.From(view!), result.Value.TemporaryPassword));
    }

    // POST /api/admin/accounts/{id}/reset-password: the body { temporaryPassword? } is optional, so a request
    // without one (and so without a Content-Type) must still reach the handler; it is read and validated here.
    private static async Task<IResult> ResetPasswordAsync(Guid id, HttpRequest http, AccountService accounts, CancellationToken cancellationToken)
    {
        var (request, problem) = await ReadOptionalBodyAsync(http, cancellationToken);
        if (problem is not null)
        {
            return problem;
        }

        var result = await accounts.ResetPasswordAsync(id, request?.TemporaryPassword, cancellationToken);
        return result.Succeeded
            ? TypedResults.Ok(new TemporaryPasswordResponse(result.Value!))
            : Problem(result.Error, result.Codes, "temporaryPassword");
    }

    private static async Task<(ResetPasswordBody? Body, IResult? Problem)> ReadOptionalBodyAsync(HttpRequest http, CancellationToken cancellationToken)
    {
        var hasBody = http.ContentLength is > 0 || (http.ContentLength is null && http.Headers.TransferEncoding.Count > 0);
        if (!hasBody)
        {
            return (null, null);
        }

        if (!http.HasJsonContentType())
        {
            return (null, Invalid("body", "Send the body as application/json."));
        }

        ResetPasswordBody? body;
        try
        {
            body = await http.ReadFromJsonAsync<ResetPasswordBody>(cancellationToken);
        }
        catch (JsonException)
        {
            return (null, Invalid("body", "The body is not valid JSON of the expected shape."));
        }

        if (body is null)
        {
            return (null, null);
        }

        var results = new List<ValidationResult>();
        if (!Validator.TryValidateObject(body, new ValidationContext(body), results, validateAllProperties: true))
        {
            var errors = results
                .SelectMany(r => r.MemberNames.DefaultIfEmpty(string.Empty).Select(name => (Name: name, r.ErrorMessage)))
                .GroupBy(e => e.Name, StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage ?? "Invalid.").ToArray(), StringComparer.Ordinal);
            return (null, TypedResults.ValidationProblem(errors));
        }

        return (body, null);
    }

    private static IResult Invalid(string field, string message) =>
        TypedResults.ValidationProblem(new Dictionary<string, string[]> { [field] = [message] });

    private static async Task<IResult> MutateAsync(
        Guid id,
        Func<Guid, CancellationToken, Task<AccountResult<Infrastructure.Identity.ApplicationUser>>> mutation,
        AdminAccountQuery query,
        TimeProvider clock,
        CancellationToken cancellationToken)
    {
        var result = await mutation(id, cancellationToken);
        if (!result.Succeeded)
        {
            return Problem(result.Error, result.Codes, "id");
        }

        var view = await query.FindAsync(id, clock.GetUtcNow(), cancellationToken);
        return TypedResults.Ok(AccountView.From(view!));
    }

    private static IResult Problem(AccountError error, IReadOnlyList<string> codes, string passwordField) => error switch
    {
        AccountError.AccountNotFound => ProblemResults.Problem(ProblemTypes.AccountNotFound, "No account has that id."),
        AccountError.StudentNotFound => ProblemResults.Problem(ProblemTypes.StudentNotFound, "No student has that number."),
        AccountError.LecturerNotFound => ProblemResults.Problem(ProblemTypes.LecturerNotFound, "No lecturer has that staff number."),
        AccountError.DemoAccount => ProblemResults.Problem(ProblemTypes.DemoAccount),
        AccountError.SelfLockout => ProblemResults.Problem(ProblemTypes.SelfLockout, "You cannot lock or disable your own account."),
        AccountError.UsernameTaken => ProblemResults.Problem(ProblemTypes.UsernameTaken, "That username is taken."),
        AccountError.PrincipalHasAccount => ProblemResults.Problem(ProblemTypes.PrincipalHasAccount, "That student or lecturer already has an account."),
        AccountError.PrincipalLeft => ProblemResults.Problem(ProblemTypes.PrincipalLeft, "That student or lecturer has left, so their account cannot be created or enabled."),
        AccountError.RolePrincipalMismatch => ProblemResults.Problem(ProblemTypes.RolePrincipalMismatch, "A Student needs a student number, a Lecturer a staff number, and an Admin neither."),
        AccountError.WeakPassword => ProblemResults.Problem(
            ProblemTypes.WeakPassword,
            extensions: new Dictionary<string, object?> { ["errors"] = new Dictionary<string, string[]> { [passwordField] = [.. codes] } }),
        _ => throw new ArgumentOutOfRangeException(nameof(error), error, "Not a refusal."),
    };
}
