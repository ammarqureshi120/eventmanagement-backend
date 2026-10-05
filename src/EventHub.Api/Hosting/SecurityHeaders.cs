using Microsoft.AspNetCore.HttpsPolicy;
using Microsoft.Extensions.Options;

namespace EventHub.Api.Hosting;

/// <summary>
/// AD-22 / NFR4: API security headers on every response. The SPA's own headers (its CSP with the theme-script
/// hash, <c>no-referrer</c> on auth routes) stay with the static host / proxy.
/// <para>
/// Headers are set in <see cref="HttpResponse.OnStarting(Func{Task})"/>: the exception handler's
/// <c>Response.Clear()</c> wipes headers written earlier, but registered callbacks survive it, so the 500 path
/// and status-code pages carry them too. <see cref="HstsOptions"/> supplies only the HSTS value and excluded
/// hosts; <c>UseHsts()</c> is not used because it writes its header directly.
/// </para>
/// </summary>
public static class SecurityHeaders
{
    public const string ContentSecurityPolicy = "default-src 'none'; frame-ancestors 'none'";

    public const string ReferrerPolicy = "strict-origin-when-cross-origin";

    /// <summary>HSTS for one year, no subdomains, no preload; the default excluded hosts (localhost, 127.0.0.1, [::1]).</summary>
    public static IServiceCollection AddSecurityHeaders(this IServiceCollection services) =>
        services.AddHsts(options =>
        {
            options.MaxAge = TimeSpan.FromDays(365);
            options.IncludeSubDomains = false;
            options.Preload = false;
        });

    /// <summary>Place right after request logging, before the exception handler.</summary>
    public static IApplicationBuilder UseSecurityHeaders(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);
        var environment = app.ApplicationServices.GetRequiredService<IWebHostEnvironment>();
        var hsts = app.ApplicationServices.GetRequiredService<IOptions<HstsOptions>>().Value;
        var isDevelopment = environment.IsDevelopment();
        var hstsValue = BuildHstsValue(hsts);

        return app.Use((context, next) =>
        {
            context.Response.OnStarting(static state =>
            {
                var (context, isDevelopment, hsts, hstsValue) = ((HttpContext, bool, HstsOptions, string))state;
                var headers = context.Response.Headers;
                headers.XContentTypeOptions = "nosniff";
                headers["Referrer-Policy"] = ReferrerPolicy;

                // Development only: the Scalar UI and the runtime document need scripts and styles.
                if (!(isDevelopment && IsDevelopmentToolPath(context.Request.Path)))
                {
                    headers.ContentSecurityPolicy = ContentSecurityPolicy;
                }

                // Not "always in Production": behind a TLS-terminating proxy Request.IsHttps is false, so the API
                // sends no HSTS and the proxy owns it (AD-21). Forwarded-headers handling is the hosting follow-up.
                if (!isDevelopment && context.Request.IsHttps && !IsExcludedHost(context.Request.Host.Host, hsts))
                {
                    headers.StrictTransportSecurity = hstsValue;
                }

                return Task.CompletedTask;
            }, (context, isDevelopment, hsts, hstsValue));

            return next(context);
        });
    }

    private static bool IsDevelopmentToolPath(PathString path) =>
        path.StartsWithSegments("/scalar", StringComparison.OrdinalIgnoreCase)
        || path.StartsWithSegments("/openapi", StringComparison.OrdinalIgnoreCase);

    private static bool IsExcludedHost(string host, HstsOptions options) =>
        options.ExcludedHosts.Any(excluded => string.Equals(excluded, host, StringComparison.OrdinalIgnoreCase));

    private static string BuildHstsValue(HstsOptions options)
    {
        var value = $"max-age={(long)options.MaxAge.TotalSeconds}";
        if (options.IncludeSubDomains)
        {
            value += "; includeSubDomains";
        }

        if (options.Preload)
        {
            value += "; preload";
        }

        return value;
    }
}
