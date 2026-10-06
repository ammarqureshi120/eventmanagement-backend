using System.Globalization;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using EventHub.Application.Common.Ports;
using EventHub.Domain.Users;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace EventHub.Api.Hosting;

/// <summary>Claim types of the session cookie (AD-16). No <c>org_id</c> for a System Administrator.</summary>
public static class SessionClaims
{
    public const string Subject = "sub";
    public const string Role = "role";
    public const string Name = "name";
    public const string IssuedAt = "iat";

    /// <summary>Organization users only (Epic 2); never issued in Story 1.4.</summary>
    public const string OrganizationId = "org_id";

    /// <summary>The user's security stamp at sign-in; a rotated stamp rejects the cookie on the next request.</summary>
    public const string SecurityStamp = "AspNet.Identity.SecurityStamp";

    /// <summary>Values of the <see cref="Role"/> claim.</summary>
    public static class Roles
    {
        public const string SystemAdministrator = nameof(UserRole.SystemAdministrator);
        public const string OrgAdministrator = nameof(UserRole.OrgAdministrator);
        public const string EventManager = nameof(UserRole.EventManager);
    }
}

/// <summary>
/// AD-16 cookie session: <c>.EventHub.Session</c> (HttpOnly, SameSite=Lax, Secure whenever the request is HTTPS,
/// path <c>/</c>), 401/403 instead of redirects on every path, and the security stamp validated on every request
/// (<see cref="SecurityStampValidatorOptions.ValidationInterval"/> = zero) in the Identity scope, rebuilding the
/// <c>role</c> claim from the <c>Users</c> row. Idle/absolute timeouts and lockout are Story 1.5.
/// </summary>
public static class AuthSetup
{
    public const string Scheme = "EventHub.Session";

    public const string CookieName = ".EventHub.Session";

    public static IServiceCollection AddEventHubAuth(this IServiceCollection services)
    {
        services.AddHttpContextAccessor();

        services.AddAuthentication(Scheme)
            .AddCookie(Scheme, options =>
            {
                options.Cookie.Name = CookieName;
                options.Cookie.HttpOnly = true;
                options.Cookie.SameSite = SameSiteMode.Lax;
                options.Cookie.Path = "/";
                options.Cookie.IsEssential = true;
                // Secure whenever the request is HTTPS (always behind TLS when hosted); plain-http local runs still
                // work. Behind a TLS-terminating proxy this needs forwarded headers (deferred with hosting, 1.1 note).
                options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;

                options.Events.OnRedirectToLogin = context => Status(context, StatusCodes.Status401Unauthorized);
                options.Events.OnRedirectToAccessDenied = context => Status(context, StatusCodes.Status403Forbidden);
                options.Events.OnRedirectToLogout = _ => Task.CompletedTask;
                options.Events.OnRedirectToReturnUrl = _ => Task.CompletedTask;
                options.Events.OnValidatePrincipal = SecurityStampValidator.ValidatePrincipalAsync;
            });

        services.Configure<SecurityStampValidatorOptions>(options => options.ValidationInterval = TimeSpan.Zero);
        services.AddScoped<ISecurityStampValidator, SessionStampValidator>();
        services.AddAuthorization();
        services.AddScoped<ISessionSignIn, SessionSignIn>();
        return services;
    }

    private static Task Status(RedirectContext<CookieAuthenticationOptions> context, int status)
    {
        context.Response.StatusCode = status;
        return Task.CompletedTask;
    }
}

/// <summary>Builds the session principal: <c>sub</c>, <c>role</c>, <c>name</c>, <c>iat</c> and the security stamp.</summary>
public static class SessionPrincipal
{
    public static ClaimsPrincipal Create(SessionAccount account, string issuedAt)
    {
        ArgumentNullException.ThrowIfNull(account);

        var identity = new ClaimsIdentity(
            [
                new Claim(SessionClaims.Subject, account.UserId.ToString()),
                new Claim(SessionClaims.Role, account.Role.ToString()),
                new Claim(SessionClaims.Name, account.DisplayName),
                new Claim(SessionClaims.IssuedAt, issuedAt, ClaimValueTypes.Integer64),
                new Claim(SessionClaims.SecurityStamp, account.SecurityStamp),
            ],
            AuthSetup.Scheme,
            SessionClaims.Name,
            SessionClaims.Role);
        return new ClaimsPrincipal(identity);
    }

    /// <summary>Unix seconds, as the <c>iat</c> claim carries them.</summary>
    public static string IssuedAt(DateTime utcNow) =>
        new DateTimeOffset(DateTime.SpecifyKind(utcNow, DateTimeKind.Utc)).ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
}

/// <summary>
/// The security-stamp validator (AD-16). Runs on every request (interval zero) through
/// <see cref="IIdentityAccount.FindSessionAccountAsync"/>, which reads the <c>Users</c> row in the Identity scope on
/// its own connection. A missing user or a stamp that no longer matches rejects the cookie (it is also deleted);
/// otherwise the principal is rebuilt from the row (fresh <c>role</c> and name, same <c>iat</c>). The cookie itself
/// is not re-issued here.
/// </summary>
public sealed class SessionStampValidator(
    IIdentityAccount accounts,
    IOptions<SecurityStampValidatorOptions> options,
    TimeProvider time) : ISecurityStampValidator
{
    public async Task ValidateAsync(CookieValidatePrincipalContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var interval = options.Value.ValidationInterval;
        if (interval > TimeSpan.Zero
            && context.Properties.IssuedUtc is { } issued
            && time.GetUtcNow() < issued + interval)
        {
            return;
        }

        var principal = context.Principal;
        var presentedStamp = principal?.FindFirstValue(SessionClaims.SecurityStamp);
        var issuedAt = principal?.FindFirstValue(SessionClaims.IssuedAt);
        SessionAccount? account = null;
        if (Guid.TryParse(principal?.FindFirstValue(SessionClaims.Subject), out var userId))
        {
            try
            {
                account = await accounts.FindSessionAccountAsync(userId, context.HttpContext.RequestAborted);
            }
            catch (OperationCanceledException) when (context.HttpContext.RequestAborted.IsCancellationRequested)
            {
                // The client went away: no session for this request, and nothing to throw out of the middleware.
                context.RejectPrincipal();
                return;
            }
        }

        if (account is null || presentedStamp is null || issuedAt is null || !StampsMatch(account.SecurityStamp, presentedStamp))
        {
            context.RejectPrincipal();
            await context.HttpContext.SignOutAsync(AuthSetup.Scheme);
            return;
        }

        context.ReplacePrincipal(SessionPrincipal.Create(account, issuedAt));
        context.ShouldRenew = false;
    }

    private static bool StampsMatch(string expected, string presented) =>
        CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(expected), Encoding.UTF8.GetBytes(presented));
}
