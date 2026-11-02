using System.Globalization;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using EventHub.Application.Auth;
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
/// <c>role</c> claim from the <c>Users</c> row. Story 1.5: a 30-minute sliding idle window renewed on every
/// authenticated request, a 12-hour absolute limit from <c>iat</c>, and inactive users rejected with
/// <c>account_disabled</c> (<see cref="SessionPolicy"/>). All expiry reads the app <see cref="TimeProvider"/>,
/// which follows <c>IClock</c>.
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

                // AD-16: idle window; the validator renews the ticket on every authenticated request.
                options.ExpireTimeSpan = SessionPolicy.IdleTimeout;
                options.SlidingExpiration = true;

                options.Events.OnRedirectToLogin = Unauthorized;
                options.Events.OnRedirectToAccessDenied = context => Status(context, StatusCodes.Status403Forbidden);
                options.Events.OnRedirectToLogout = _ => Task.CompletedTask;
                options.Events.OnRedirectToReturnUrl = _ => Task.CompletedTask;
                options.Events.OnValidatePrincipal = SecurityStampValidator.ValidatePrincipalAsync;
            });

        // The cookie handler's "now" (ticket issue and expiry) is the IClock-backed app TimeProvider.
        services.AddOptions<CookieAuthenticationOptions>(Scheme)
            .Configure<TimeProvider>((options, time) => options.TimeProvider = time);

        services.Configure<SecurityStampValidatorOptions>(options => options.ValidationInterval = TimeSpan.Zero);
        services.AddScoped<ISecurityStampValidator, SessionStampValidator>();
        services.AddAuthorization();
        services.AddScoped<ISessionSignIn, SessionSignIn>();
        return services;
    }

    /// <summary>
    /// 401 for every challenge. A request that still carries a session cookie the handler refused (idle window
    /// passed, unreadable ticket) gets that cookie deleted too, with the scheme's own cookie options, unless a
    /// sign-out on this request already deleted it (AD-16: every rejection deletes the cookie).
    /// </summary>
    private static Task Unauthorized(RedirectContext<CookieAuthenticationOptions> context)
    {
        var options = context.Options;
        var name = options.Cookie.Name!;
        var alreadyDeleted = context.Response.Headers.SetCookie
            .Any(value => value is not null && value.StartsWith(name + "=", StringComparison.Ordinal));
        if (!alreadyDeleted && options.CookieManager.GetRequestCookie(context.HttpContext, name) is not null)
        {
            options.CookieManager.DeleteCookie(context.HttpContext, name, options.Cookie.Build(context.HttpContext));
        }

        return Status(context, StatusCodes.Status401Unauthorized);
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

    /// <summary>The UTC time an <c>iat</c> claim value (Unix seconds) stands for; null when it is missing or unreadable.</summary>
    public static DateTime? IssuedAtUtc(string? issuedAt) =>
        long.TryParse(issuedAt, NumberStyles.None, CultureInfo.InvariantCulture, out var seconds)
        && seconds >= 0 && seconds <= DateTimeOffset.MaxValue.ToUnixTimeSeconds()
            ? DateTimeOffset.FromUnixTimeSeconds(seconds).UtcDateTime
            : null;

    /// <summary>Unix seconds, as the <c>iat</c> claim carries them.</summary>
    public static string IssuedAt(DateTime utcNow) =>
        new DateTimeOffset(DateTime.SpecifyKind(utcNow, DateTimeKind.Utc)).ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
}

/// <summary>
/// The security-stamp validator (AD-16). Runs on every request (interval zero) through
/// <see cref="IIdentityAccount.FindSessionAccountAsync"/>, which reads the <c>Users</c> row in the Identity scope on
/// its own connection. Each rejection also deletes the cookie:
/// <list type="bullet">
/// <item><c>iat</c> + 12 h reached (absolute limit), a missing user or a stamp that no longer matches: a bare 401,
/// which becomes <c>session_expired</c>.</item>
/// <item>a user who is no longer Active: the request is marked (<see cref="DisabledItemKey"/>), so the 401 becomes
/// <c>account_disabled</c>. Checked before the stamp, because deactivation also rotates it.</item>
/// </list>
/// Otherwise the principal is rebuilt from the row (fresh <c>role</c> and name, same <c>iat</c>) and the cookie is
/// re-issued with a fresh 30-minute idle window, so idle time counts from the last authenticated request.
/// </summary>
public sealed class SessionStampValidator(
    IIdentityAccount accounts,
    IOptions<SecurityStampValidatorOptions> options,
    TimeProvider time) : ISecurityStampValidator
{
    /// <summary><c>HttpContext.Items</c> key set when the session was rejected because the user is not Active.</summary>
    public const string DisabledItemKey = "eventhub.auth.disabled";

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
        var now = time.GetUtcNow();
        if (SessionPrincipal.IssuedAtUtc(issuedAt) is not { } issuedAtUtc
            || now >= new DateTimeOffset(SessionPolicy.ExpiresAt(issuedAtUtc))
            || context.Properties.ExpiresUtc is not { } idleEnd
            || now >= idleEnd)
        {
            // AD-16 absolute limit (or no readable iat), or the idle window reached exactly at its end (the cookie
            // handler only drops tickets strictly past it): ends however active the session was.
            await RejectAsync(context);
            return;
        }

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

        if (account is null)
        {
            await RejectAsync(context);
            return;
        }

        if (!account.IsActive)
        {
            // FR2: deactivated mid-session. Before the stamp check: deactivation rotates the stamp too.
            context.HttpContext.Items[DisabledItemKey] = true;
            await RejectAsync(context);
            return;
        }

        if (presentedStamp is null || !StampsMatch(account.SecurityStamp, presentedStamp))
        {
            await RejectAsync(context);
            return;
        }

        context.ReplacePrincipal(SessionPrincipal.Create(account, issuedAt!));

        // AD-16 sliding idle window: re-issue the cookie on every authenticated request (not only after half the
        // window), so a request at 29 minutes keeps the session alive for another 30.
        context.Properties.IssuedUtc = now;
        context.Properties.ExpiresUtc = now + SessionPolicy.IdleTimeout;
        context.ShouldRenew = true;
    }

    private static async Task RejectAsync(CookieValidatePrincipalContext context)
    {
        context.RejectPrincipal();
        await context.HttpContext.SignOutAsync(AuthSetup.Scheme);
    }

    private static bool StampsMatch(string expected, string presented) =>
        CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(expected), Encoding.UTF8.GetBytes(presented));
}
