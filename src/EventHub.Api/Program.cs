using EventHub.Api.Endpoints.Auth;
using EventHub.Api.Hosting;
using EventHub.Api.Logging;
using EventHub.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.OpenApi;
using Scalar.AspNetCore;
using Serilog;
using Serilog.Events;

// Bootstrap logger for startup failures only; the configured logger replaces it via AddSerilog.
Log.Logger = new LoggerConfiguration()
    .Enrich.With<SensitiveDataRedactionEnricher>()
    .WriteTo.Console()
    .CreateLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);

    builder.AddServiceDefaults();
    builder.AddEventHubSerilog();

    builder.Services.AddInfrastructure(builder.Configuration, DocumentGeneration.IsRunning);

    // AD-4: OpenAPI 3.0 pinned at runtime (build time is pinned in the csproj).
    builder.Services.AddOpenApi(options => options.OpenApiVersion = OpenApiSpecVersion.OpenApi3_0);

    // AD-16: SPA reads the XSRF-TOKEN cookie and echoes it in X-XSRF-TOKEN on unsafe verbs.
    builder.Services.AddAntiforgery(options =>
    {
        options.HeaderName = AntiforgeryEndpoints.HeaderName;
        options.Cookie.Name = ".EventHub.Antiforgery";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
        // Secure whenever the request is HTTPS (always in deployed envs behind TLS); plain-http local runs still work.
        options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
    });

    var app = builder.Build();

    // AD-4: migrations and seeding (Story 1.3+) must never run under the build-time document generator.
    if (!DocumentGeneration.IsRunning)
    {
        // Startup work that touches the database or Aspire resources goes here.
    }

    // Health probes run every few seconds; keep them out of normal request logs.
    app.UseSerilogRequestLogging(options => options.GetLevel = (context, _, exception) =>
        exception is not null || context.Response.StatusCode >= 500 && !IsHealthPath(context)
            ? LogEventLevel.Error
            : IsHealthPath(context) ? LogEventLevel.Verbose : LogEventLevel.Information);

    if (app.Environment.IsDevelopment())
    {
        app.MapOpenApi();
        app.MapScalarApiReference();
    }

    app.MapDefaultEndpoints();

    var api = app.MapGroup("/api");
    api.MapAuthEndpoints();

    app.Run();
}
catch (Exception ex) when (ex is not HostAbortedException)
{
    Log.Fatal(ex, "EventHub API terminated unexpectedly");
    throw;
}
finally
{
    Log.CloseAndFlush();
}

static bool IsHealthPath(HttpContext context) => context.Request.Path.StartsWithSegments("/health");

public partial class Program;
