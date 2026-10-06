using EventHub.Application.Common.Ports;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.Extensions.Options;

namespace EventHub.Api.Hosting;

/// <summary>
/// AD-16 adapter for the Application port <see cref="ISessionSignIn"/>. Both calls only stage <c>Set-Cookie</c>
/// headers; the response starts after the pipeline (and its transaction) returns.
/// <list type="bullet">
/// <item>Sign-in: if the command fails afterwards, the exception handler's <c>Response.Clear()</c> drops the staged
/// cookie, so a session is only ever issued for a committed sign-in.</item>
/// <item>Sign-out: the deletion must survive an error after it (stamp rotation failing → 500), so it is re-applied
/// from an <c>OnStarting</c> callback whenever <c>Response.Clear()</c> removed it.</item>
/// </list>
/// </summary>
public sealed class SessionSignIn(
    IHttpContextAccessor accessor,
    IClock clock,
    IOptionsMonitor<CookieAuthenticationOptions> cookieOptions) : ISessionSignIn
{
    public async Task SignInAsync(SessionAccount account, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(account);
        var context = Context();
        var principal = SessionPrincipal.Create(account, SessionPrincipal.IssuedAt(clock.UtcNow));
        await context.SignInAsync(AuthSetup.Scheme, principal, new AuthenticationProperties { IsPersistent = false });
    }

    public async Task SignOutAsync(CancellationToken cancellationToken = default)
    {
        var context = Context();
        await context.SignOutAsync(AuthSetup.Scheme);

        var options = cookieOptions.Get(AuthSetup.Scheme);
        context.Response.OnStarting(static state =>
        {
            var (context, options) = ((HttpContext, CookieAuthenticationOptions))state;
            var name = options.Cookie.Name!;
            var alreadyDeleted = context.Response.Headers.SetCookie
                .Any(value => value is not null && value.StartsWith(name + "=", StringComparison.Ordinal));
            if (!alreadyDeleted)
            {
                // The scheme's cookie manager also deletes chunked cookies (name + C1, C2, ...).
                options.CookieManager.DeleteCookie(context, name, options.Cookie.Build(context));
            }

            return Task.CompletedTask;
        }, (context, options));
    }

    private HttpContext Context() =>
        accessor.HttpContext ?? throw new InvalidOperationException("Session sign-in needs an HTTP request.");
}
