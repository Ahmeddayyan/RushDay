using System.Collections.Frozen;

namespace RushDay.Api.Security;

/// <summary>
/// The closed slug catalogue of 02-api.md section 6 (D16): exactly these 63 slugs, each surfaced as
/// <c>type = "urn:rushday:&lt;slug&gt;"</c>. Adding one is a spec change; <c>ProblemTypesTests</c> asserts the set.
/// </summary>
public static class ProblemTypes
{
    public const string UrnPrefix = "urn:rushday:";

    // 400
    public const string Validation = "validation";
    public const string Antiforgery = "antiforgery";
    public const string InvalidCurrentPassword = "invalid-current-password";
    public const string WeakPassword = "weak-password";
    public const string InvalidMfaCode = "invalid-mfa-code";

    // 401
    public const string Unauthenticated = "unauthenticated";
    public const string InvalidCredentials = "invalid-credentials";

    // 403
    public const string Forbidden = "forbidden";
    public const string NotYourModule = "not-your-module";
    public const string NotModuleLeader = "not-module-leader";
    public const string PasswordChangeRequired = "password-change-required";
    public const string MfaSetupRequired = "mfa-setup-required";

    // 404
    public const string NotFound = "not-found";
    public const string ModuleNotFound = "module-not-found";
    public const string StudentNotFound = "student-not-found";
    public const string LecturerNotFound = "lecturer-not-found";
    public const string AccountNotFound = "account-not-found";
    public const string WindowNotFound = "window-not-found";
    public const string PublicationNotFound = "publication-not-found";
    public const string AnnouncementNotFound = "announcement-not-found";
    public const string GradeNotFound = "grade-not-found";
    public const string NotEnrolled = "not-enrolled";

    // 405
    public const string MethodNotAllowed = "method-not-allowed";

    // 409
    public const string AlreadyEnrolled = "already-enrolled";
    public const string ModuleFull = "module-full";
    public const string ModuleInactive = "module-inactive";
    public const string EnrolmentWindowClosed = "enrolment-window-closed";
    public const string WithdrawalDeadlinePassed = "withdrawal-deadline-passed";
    public const string ResultsExist = "results-exist";
    public const string StudentLeft = "student-left";
    public const string ModuleLocked = "module-locked";
    public const string ModuleNotSubmitted = "module-not-submitted";
    public const string AlreadySubmitted = "already-submitted";
    public const string NothingToSubmit = "nothing-to-submit";
    public const string StaleMark = "stale-mark";
    public const string NothingToPublish = "nothing-to-publish";
    public const string PublicationLive = "publication-live";
    public const string PublicationScheduled = "publication-scheduled";
    public const string UsernameTaken = "username-taken";
    public const string PrincipalHasAccount = "principal-has-account";
    public const string PrincipalLeft = "principal-left";
    public const string ModuleCodeTaken = "module-code-taken";
    public const string StudentNumberTaken = "student-number-taken";
    public const string StaffNumberTaken = "staff-number-taken";
    public const string WindowExists = "window-exists";
    public const string DemoAccount = "demo-account";
    public const string MfaAlreadyEnabled = "mfa-already-enabled";

    // 413, 415
    public const string PayloadTooLarge = "payload-too-large";
    public const string UnsupportedMediaType = "unsupported-media-type";

    // 422
    public const string CreditLimitExceeded = "credit-limit-exceeded";
    public const string MarksIncomplete = "marks-incomplete";
    public const string NotEnrolledStudents = "not-enrolled-students";
    public const string CapacityBelowEnrolled = "capacity-below-enrolled";
    public const string SemesterChangeWithEnrolments = "semester-change-with-enrolments";
    public const string PublishTooFarAhead = "publish-too-far-ahead";
    public const string InvalidLecturerAssignment = "invalid-lecturer-assignment";
    public const string WindowDatesInvalid = "window-dates-invalid";
    public const string SelfLockout = "self-lockout";
    public const string RolePrincipalMismatch = "role-principal-mismatch";

    // 429, 500, 503
    public const string RateLimited = "rate-limited";
    public const string InternalError = "internal-error";
    public const string ServerBusy = "server-busy";
    public const string Timeout = "timeout";

    /// <summary>Every slug with its HTTP status.</summary>
    public static FrozenDictionary<string, int> StatusBySlug { get; } = new Dictionary<string, int>
    {
        [Validation] = 400, [Antiforgery] = 400, [InvalidCurrentPassword] = 400, [WeakPassword] = 400, [InvalidMfaCode] = 400,
        [Unauthenticated] = 401, [InvalidCredentials] = 401,
        [Forbidden] = 403, [NotYourModule] = 403, [NotModuleLeader] = 403, [PasswordChangeRequired] = 403, [MfaSetupRequired] = 403,
        [NotFound] = 404, [ModuleNotFound] = 404, [StudentNotFound] = 404, [LecturerNotFound] = 404, [AccountNotFound] = 404,
        [WindowNotFound] = 404, [PublicationNotFound] = 404, [AnnouncementNotFound] = 404, [GradeNotFound] = 404, [NotEnrolled] = 404,
        [MethodNotAllowed] = 405,
        [AlreadyEnrolled] = 409, [ModuleFull] = 409, [ModuleInactive] = 409, [EnrolmentWindowClosed] = 409,
        [WithdrawalDeadlinePassed] = 409, [ResultsExist] = 409, [StudentLeft] = 409, [ModuleLocked] = 409,
        [ModuleNotSubmitted] = 409, [AlreadySubmitted] = 409, [NothingToSubmit] = 409, [StaleMark] = 409,
        [NothingToPublish] = 409, [PublicationLive] = 409, [PublicationScheduled] = 409, [UsernameTaken] = 409,
        [PrincipalHasAccount] = 409, [PrincipalLeft] = 409, [ModuleCodeTaken] = 409, [StudentNumberTaken] = 409, [StaffNumberTaken] = 409,
        [WindowExists] = 409, [DemoAccount] = 409, [MfaAlreadyEnabled] = 409,
        [PayloadTooLarge] = 413,
        [UnsupportedMediaType] = 415,
        [CreditLimitExceeded] = 422, [MarksIncomplete] = 422, [NotEnrolledStudents] = 422, [CapacityBelowEnrolled] = 422,
        [SemesterChangeWithEnrolments] = 422, [PublishTooFarAhead] = 422, [InvalidLecturerAssignment] = 422,
        [WindowDatesInvalid] = 422, [SelfLockout] = 422, [RolePrincipalMismatch] = 422,
        [RateLimited] = 429,
        [InternalError] = 500,
        [ServerBusy] = 503, [Timeout] = 503,
    }.ToFrozenDictionary(StringComparer.Ordinal);

    public static IReadOnlyCollection<string> All => StatusBySlug.Keys;

    public static string Urn(string slug) => UrnPrefix + slug;

    /// <summary>The slug for a problem the framework produced (02-api.md section 6), by status code.</summary>
    public static string ForStatus(int status) => status switch
    {
        400 => Validation,
        401 => Unauthenticated,
        403 => Forbidden,
        404 => NotFound,
        405 => MethodNotAllowed,
        413 => PayloadTooLarge,
        415 => UnsupportedMediaType,
        429 => RateLimited,
        503 => ServerBusy,
        _ => InternalError,
    };

    /// <summary>A short human title: the slug with its first letter capitalised and hyphens as spaces.</summary>
    public static string TitleOf(string slug) =>
        string.IsNullOrEmpty(slug) ? "Error" : char.ToUpperInvariant(slug[0]) + slug[1..].Replace('-', ' ');
}
