using EventHub.Api.Endpoints.Auth;
using EventHub.Api.Errors;
using EventHub.Api.Hosting;
using EventHub.Api.Logging;
using EventHub.Application.Common.Ports;
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

    // AD-4: under the build-time document generator no migrations or seeding are registered.
    builder.Services.AddInfrastructure(builder.Configuration, DocumentGeneration.IsRunning);

    // AD-5 / AD-8: Mediator with the fixed pipeline, the permission matrix and validators.
    builder.Services.AddEventHubApplication();
    builder.Services.AddScoped<ICurrentUser, HttpCurrentUser>();

    // AD-17: every non-2xx under /api is an EventHubProblem with code + traceId and no internals.
    builder.Services.AddProblemDetails(options => options.CustomizeProblemDetails = EventHubProblem.Customize);
    builder.Services.AddExceptionHandler<EventHubExceptionHandler>();

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

    // AD-4: migrations and seeding run as hosted services (DatabaseMigrator, then SystemAdministratorSeeder),
    // registered by AddInfrastructure only when the build-time document generator is not running.

    // Health probes run every few seconds; keep them out of normal request logs. Request logging sits outside the
    // exception handler so it records the final status without the exception (whose message may hold row data).
    app.UseSerilogRequestLogging(options => options.GetLevel = (context, _, exception) =>
        exception is not null || context.Response.StatusCode >= 500 && !IsHealthPath(context)
            ? LogEventLevel.Error
            : IsHealthPath(context) ? LogEventLevel.Verbose : LogEventLevel.Information);

    app.UseExceptionHandler();
    app.UseWhen(
        context => context.Request.Path.StartsWithSegments("/api"),
        api => api.UseStatusCodePages());

    if (app.Environment.IsDevelopment())
    {
        app.MapOpenApi();
        app.MapScalarApiReference();
    }

    app.MapDefaultEndpoints();

    var api = app.MapGroup("/api");
    api.MapAuthEndpoints();
    foreach (var module in app.Services.GetServices<IApiEndpointModule>())
    {
        module.MapEndpoints(api);
    }

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
