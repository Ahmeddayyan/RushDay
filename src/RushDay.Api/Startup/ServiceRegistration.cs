using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.Http.Timeouts;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Server.Kestrel.Transport.Sockets;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using RushDay.Api.Auth;
using RushDay.Api.Contracts;
using RushDay.Api.Endpoints;
using RushDay.Api.Observability;
using RushDay.Api.Options;
using RushDay.Api.Security;
using RushDay.Infrastructure;
using RushDay.Infrastructure.Accounts;
using RushDay.Infrastructure.Audit;
using RushDay.Infrastructure.Caching;
using RushDay.Infrastructure.Identity;
using RushDay.Infrastructure.Persistence;
using ApiDataProtectionOptions = RushDay.Api.Options.DataProtectionOptions;

namespace RushDay.Api.Startup;

/// <summary>
/// Every service of the API (02-api.md sections 2–6, 03-security.md sections 3–6, 04-performance-and-ops.md sections
/// 4–6). Options that depend on the environment or configuration are configured lazily through the options system, so
/// the final configuration (test overrides included) is what counts.
/// </summary>
public static partial class ServiceRegistration
{
    public const string LoginTimeoutPolicy = "login";
    public const string ExportTimeoutPolicy = "export";

    public static readonly TimeSpan DefaultRequestTimeout = TimeSpan.FromSeconds(15);
    public static readonly TimeSpan LoginRequestTimeout = TimeSpan.FromSeconds(20);
    public static readonly TimeSpan ExportRequestTimeout = TimeSpan.FromSeconds(60);

    /// <summary>Every 503 carries <c>Retry-After</c> (02-api.md section 5); a timed-out request may retry after 2 s.</summary>
    public const int TimeoutRetryAfterSeconds = 2;

    public static WebApplicationBuilder AddRushDayServices(this WebApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var services = builder.Services;
        var configuration = builder.Configuration;

        AddOptions(services, configuration);
        services.AddSingleton(TimeProvider.System);
        services.AddHttpContextAccessor();

        ConfigureLogging(builder);
        ConfigureKestrel(services);
        ConfigureJson(services);

        services.AddValidation();

        // A body that does not bind (malformed JSON, a wrong type, no body) is a 400 validation problem in every
        // environment; Development's default would throw it into the exception handler as a 500.
        services.Configure<RouteHandlerOptions>(o => o.ThrowOnBadRequest = false);
        services.AddProblemDetails(o => o.CustomizeProblemDetails = ProblemDetailsCustomizer.Customize);
        services.AddExceptionHandler<RushDayExceptionHandler>();
        services.AddRequestTimeouts(o =>
        {
            o.DefaultPolicy = TimeoutPolicy(DefaultRequestTimeout);
            o.AddPolicy(LoginTimeoutPolicy, TimeoutPolicy(LoginRequestTimeout));
            o.AddPolicy(ExportTimeoutPolicy, TimeoutPolicy(ExportRequestTimeout));
        });
        services.AddOpenApi();

        AddPersistence(services, configuration, builder.Environment);
        AddIdentity(services);
        AddDataProtection(services);
        AddSecurity(services);
        AddObservability(services);
        AddCaching(services);

        services.AddScoped<CurrentUser>();
        services.AddScoped<IAuditContext, HttpAuditContext>();
        services.AddScoped<AuditWriter>();
        services.AddScoped<AccountService>();
        services.AddSingleton<LoginThrottle>();

        AddStudentSurface(services);
        AddStaffSurface(services);

        return builder;
    }

    // Stages S4 and S6 register their services by implementing these in their own files (06 ownership rules):
    // `public static partial class ServiceRegistration { static partial void AddStudentSurface(IServiceCollection services) { ... } }`.
    static partial void AddStudentSurface(IServiceCollection services);

    static partial void AddStaffSurface(IServiceCollection services);

    private static void AddOptions(IServiceCollection services, IConfiguration configuration)
    {
        // Outside Development the app migrates and backfills on boot unless told otherwise (01 section 6, 03 section 8):
        // these defaults are applied before the section is bound, so an explicit false still wins.
        services.AddOptions<DatabaseOptions>()
            .Configure<IHostEnvironment>((o, environment) =>
            {
                if (!environment.IsDevelopment())
                {
                    o.MigrateOnStartup = true;
                    o.BackfillOnStartup = true;
                }
            })
            .Bind(configuration.GetSection(DatabaseOptions.SectionName));
        services.AddOptions<RateLimitingOptions>().Bind(configuration.GetSection(RateLimitingOptions.SectionName));
        services.AddOptions<DemoOptions>().Bind(configuration.GetSection(DemoOptions.SectionName));
        services.AddOptions<BootstrapOptions>().Bind(configuration.GetSection(BootstrapOptions.SectionName));
        services.AddOptions<BrandingOptions>().Bind(configuration.GetSection(BrandingOptions.SectionName));
        services.AddOptions<AuthOptions>().Bind(configuration.GetSection(AuthOptions.SectionName));
        services.AddOptions<SecurityOptions>().Bind(configuration.GetSection(SecurityOptions.SectionName));
        services.AddOptions<ApiDataProtectionOptions>().Bind(configuration.GetSection(ApiDataProtectionOptions.SectionName));
    }

    private static void ConfigureLogging(WebApplicationBuilder builder)
    {
        // Production: one JSON object per line; Development: plain console. Scopes stay off: the hosting scope carries
        // the raw request path (and so any personal data in it), and the per-request line already carries traceId,
        // userId, role and the route template (03-security.md section 6).
        if (!builder.Environment.IsDevelopment())
        {
            builder.Logging.ClearProviders();
            builder.Logging.AddJsonConsole(o =>
            {
                o.IncludeScopes = false;
                o.UseUtcTimestamp = true;
                o.TimestampFormat = "yyyy-MM-ddTHH:mm:ss.fffZ ";
            });
        }
    }

    private static void ConfigureKestrel(IServiceCollection services)
    {
        services.Configure<Microsoft.AspNetCore.Server.Kestrel.Core.KestrelServerOptions>(o =>
        {
            o.Limits.MaxRequestBodySize = 262_144;
            o.Limits.MaxConcurrentConnections = 2_000;
            o.Limits.KeepAliveTimeout = TimeSpan.FromSeconds(120);
            o.Limits.RequestHeadersTimeout = TimeSpan.FromSeconds(30);
        });
        services.Configure<SocketTransportOptions>(o => o.Backlog = 1_024);
    }

    private static void ConfigureJson(IServiceCollection services)
    {
        services.ConfigureHttpJsonOptions(o =>
        {
            var json = o.SerializerOptions;
            json.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
            json.DictionaryKeyPolicy = null;
            json.DefaultIgnoreCondition = JsonIgnoreCondition.Never;
            json.MaxDepth = 16;
            json.UnmappedMemberHandling = JsonUnmappedMemberHandling.Skip;
            json.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
            json.Converters.Add(new UtcDateTimeOffsetJsonConverter());
        });
    }

    private static void AddPersistence(IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        var connectionString = configuration.GetConnectionString("RushDay")
            ?? throw new InvalidOperationException("Connection string 'RushDay' is not configured (ConnectionStrings__RushDay).");
        var database = configuration.GetSection(DatabaseOptions.SectionName).Get<DatabaseOptions>() ?? new DatabaseOptions();

        // Outside Development "Include Error Detail" is forced off, whatever the connection string says (T10).
        services.AddRushDayPersistence(connectionString, StartupTasks.MaxPoolSize(database), allowErrorDetail: environment.IsDevelopment());

        // For work that must not borrow a request's scoped context, above all HybridCache factories (04 section 4).
        services.AddSingleton<IDbContextFactory<RushDayDbContext>, RushDayDbContextFactory>();
        services.AddSingleton<DbCommandCounter>();
        services.ConfigureDbContext<RushDayDbContext>((provider, options) => options.AddInterceptors(provider.GetRequiredService<DbCommandCounter>()));
        services.AddHealthChecks().AddDbContextCheck<RushDayDbContext>("database");
    }

    private static void AddIdentity(IServiceCollection services)
    {
        // 02-api.md section 2.1, exactly.
        services.AddIdentityCore<ApplicationUser>(o =>
            {
                o.Password.RequiredLength = RushDayPasswordValidator.MinimumLength;
                o.Password.RequireDigit = false;
                o.Password.RequireUppercase = false;
                o.Password.RequireLowercase = false;
                o.Password.RequireNonAlphanumeric = false;
                o.Password.RequiredUniqueChars = RushDayPasswordValidator.RequiredUniqueChars;
                o.Lockout.AllowedForNewUsers = true;
                o.Lockout.MaxFailedAccessAttempts = 5;
                o.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
                o.User.AllowedUserNameCharacters = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789._-";
                o.User.RequireUniqueEmail = false;
                o.ClaimsIdentity.RoleClaimType = RushDayClaims.Role;
                o.ClaimsIdentity.UserIdClaimType = RushDayClaims.Subject;
                o.ClaimsIdentity.UserNameClaimType = RushDayClaims.Name;
                o.Tokens.AuthenticatorTokenProvider = TokenOptions.DefaultAuthenticatorProvider;
            })
            .AddRoles<IdentityRole<Guid>>()
            .AddEntityFrameworkStores<RushDayDbContext>()
            .AddSignInManager()
            // TOTP only (no email or phone providers), refusing a time step that was already accepted.
            .AddTokenProvider<ReplayProtectedAuthenticatorTokenProvider>(TokenOptions.DefaultAuthenticatorProvider)
            .AddPasswordValidator<RushDayPasswordValidator>()
            .AddClaimsPrincipalFactory<RushDayClaimsPrincipalFactory>();
        services.Configure<PasswordHasherOptions>(o => o.IterationCount = PasswordHashing.IterationCount);
        services.AddScoped<ISecurityStampValidator, RushDaySecurityStampValidator>();

        services.AddAuthentication(IdentityConstants.ApplicationScheme).AddIdentityCookies();
        services.AddOptions<CookieAuthenticationOptions>(IdentityConstants.ApplicationScheme)
            .Configure<IHostEnvironment, IOptions<AuthOptions>>((o, environment, auth) =>
            {
                o.Cookie.Name = CookiePrefix(environment) + "rushday.auth";
                o.Cookie.HttpOnly = true;
                o.Cookie.SameSite = SameSiteMode.Strict;
                o.Cookie.SecurePolicy = SecurePolicy(environment);
                o.Cookie.Path = "/";
                o.ExpireTimeSpan = TimeSpan.FromHours(auth.Value.SessionAbsoluteHours);
                o.SlidingExpiration = false;
                o.Events.OnRedirectToLogin = ctx => ProblemResults.WriteAsync(ctx.HttpContext, StatusCodes.Status401Unauthorized, ProblemTypes.Unauthenticated);
                o.Events.OnRedirectToAccessDenied = ctx => ProblemResults.WriteAsync(ctx.HttpContext, StatusCodes.Status403Forbidden, ProblemTypes.Forbidden);
                o.Events.OnValidatePrincipal = RushDayCookieEvents.ValidateAsync;
            });
        services.AddOptions<CookieAuthenticationOptions>(IdentityConstants.TwoFactorUserIdScheme)
            .Configure<IHostEnvironment, IOptions<AuthOptions>>((o, environment, auth) =>
            {
                o.Cookie.Name = CookiePrefix(environment) + "rushday.mfa";
                o.Cookie.HttpOnly = true;
                o.Cookie.SameSite = SameSiteMode.Strict;
                o.Cookie.SecurePolicy = SecurePolicy(environment);
                o.Cookie.Path = "/";

                // The challenge lives exactly MfaCookieMinutes from the password step: verify calls never renew it.
                o.ExpireTimeSpan = TimeSpan.FromMinutes(auth.Value.MfaCookieMinutes);
                o.SlidingExpiration = false;

                // ...and it is bound to the security stamp, so a reset or change of the password kills it.
                o.Events.OnSigningIn = MfaChallengeBinding.OnSigningInAsync;
            });
        services.AddOptions<SecurityStampValidatorOptions>()
            .Configure<IOptions<AuthOptions>>((o, auth) =>
            {
                o.ValidationInterval = TimeSpan.FromMinutes(auth.Value.SecurityStampIntervalMinutes);
                o.OnRefreshingPrincipal = ctx =>
                {
                    RushDayCookieEvents.CopyLifetimeClaims(ctx.CurrentPrincipal, ctx.NewPrincipal);
                    return Task.CompletedTask;
                };
            });

        services.AddAntiforgery();
        services.AddOptions<Microsoft.AspNetCore.Antiforgery.AntiforgeryOptions>()
            .Configure<IHostEnvironment>((o, environment) =>
            {
                o.HeaderName = "X-CSRF-TOKEN";
                o.Cookie.Name = CookiePrefix(environment) + "rushday.csrf";
                o.Cookie.HttpOnly = true;
                o.Cookie.SameSite = SameSiteMode.Strict;
                o.Cookie.SecurePolicy = SecurePolicy(environment);
                o.Cookie.Path = "/";
                o.SuppressXFrameOptionsHeader = true;
            });

        services.AddAuthorization(o =>
        {
            o.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build();
            Policies.Register(o);
        });
        services.AddScoped<IAuthorizationHandler, TeachesModuleHandler>();
        services.AddSingleton<Microsoft.AspNetCore.Authorization.IAuthorizationMiddlewareResultHandler, RushDayAuthorizationResultHandler>();
    }

    private static void AddDataProtection(IServiceCollection services)
    {
        services.AddDataProtection()
            .SetApplicationName("RushDay")
            .PersistKeysToDbContext<RushDayDbContext>();

        services.AddSingleton(provider => new DataProtectionKeyEncryptionKeyHolder(
            DataProtectionKeyEncryptionKey.FromConfiguration(provider.GetRequiredService<IOptions<ApiDataProtectionOptions>>().Value.KeyEncryptionKey)));

        // Outside Development every new key is encrypted under the KEK (D31); StartupTasks refuses to start without it.
        services.AddOptions<KeyManagementOptions>()
            .Configure<IHostEnvironment, DataProtectionKeyEncryptionKeyHolder>((o, environment, holder) =>
            {
                if (!environment.IsDevelopment())
                {
                    o.XmlEncryptor = new AesGcmXmlEncryptor(holder.Key ?? throw new InvalidOperationException(DataProtectionKeyEncryptionKey.MissingMessage));
                }
            });
    }

    private static void AddSecurity(IServiceCollection services)
    {
        services.AddSingleton<IpHasher>();
        services.AddRushDayHostFiltering();

        services.AddOptions<ForwardedHeadersOptions>()
            .Configure<IOptions<SecurityOptions>>((o, security) =>
            {
                o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
                o.ForwardLimit = 1;
                if (security.Value.TrustForwardedHeaders)
                {
                    o.KnownIPNetworks.Clear();
                    o.KnownProxies.Clear();
                }
            });

        services.AddHsts(_ => { });
        services.AddOptions<Microsoft.AspNetCore.HttpsPolicy.HstsOptions>()
            .Configure<IOptions<SecurityOptions>>((o, security) =>
            {
                o.MaxAge = TimeSpan.FromDays(365);
                o.IncludeSubDomains = security.Value.HstsIncludeSubDomains;
                o.Preload = security.Value.HstsPreload;
            });

        services.AddRateLimiter(_ => { });
        services.AddOptions<RateLimiterOptions>()
            .Configure<IOptions<RateLimitingOptions>, RushDayMetrics>((o, limits, metrics) => RateLimitPolicies.Configure(o, limits.Value, metrics));

        services.AddOutputCache(o => o.AddPolicy(PublicEndpoints.StatusCachePolicy, new PublicStatusCachePolicy()));
    }

    private static void AddObservability(IServiceCollection services)
    {
        services.AddMetrics();
        services.AddSingleton<RushDayMetrics>();
        services.AddSingleton<ICacheMetrics>(provider => provider.GetRequiredService<RushDayMetrics>());
        services.AddSingleton<MetricsSnapshotService>();
        services.AddHostedService(provider => provider.GetRequiredService<MetricsSnapshotService>());
    }

    private static void AddCaching(IServiceCollection services)
    {
        services.AddHybridCache();
        services.AddScoped<SettingsCache>();
        services.AddScoped<EnrolmentWindowCache>();
        services.AddScoped<PublicationCache>();
        services.AddScoped<LecturerModuleCache>();
    }

    private static string CookiePrefix(IHostEnvironment environment) => environment.IsDevelopment() ? string.Empty : "__Host-";

    private static CookieSecurePolicy SecurePolicy(IHostEnvironment environment) =>
        environment.IsDevelopment() ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;

    private static RequestTimeoutPolicy TimeoutPolicy(TimeSpan timeout) => new()
    {
        Timeout = timeout,
        TimeoutStatusCode = StatusCodes.Status503ServiceUnavailable,
        WriteTimeoutResponse = context => ProblemResults.WriteAsync(context, StatusCodes.Status503ServiceUnavailable, ProblemTypes.Timeout, retryAfterSeconds: TimeoutRetryAfterSeconds),
    };
}
