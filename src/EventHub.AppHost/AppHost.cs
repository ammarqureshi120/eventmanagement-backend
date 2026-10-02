// AD-3 / AD-21: local-dev orchestration only. No containers: SQL Server 2025 Developer and
// Mailpit run natively on Windows; Aspire starts Mailpit, the API and Vite and wires them together.
using EventHub.AppHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

var builder = DistributedApplication.CreateBuilder(args);

// SQL Server: the locally installed instance. Override with
// `dotnet user-secrets set "ConnectionStrings:eventhub" "<connection string>"`.
var database = builder.AddConnectionString("eventhub");
var sqlConnectionString = builder.Configuration.GetConnectionString("eventhub");

builder.Services.AddHealthChecks()
    .AddCheck("eventhub-sql", new LocalSqlHealthCheck(sqlConnectionString));
database.WithHealthCheck("eventhub-sql");

// Dev convenience (what a SQL container's AddDatabase used to do): create the empty database once.
// Schema, migrations and seeding are the API's job (Story 1.3+).
builder.Eventing.Subscribe<BeforeStartEvent>(async (_, cancellationToken) =>
    await LocalSqlDatabase.TryEnsureCreatedAsync(sqlConnectionString, cancellationToken));

// Mailpit: native binary (`mailpit.exe` on PATH, or "Mailpit:Path" in config/user-secrets).
var mailpitPath = builder.Configuration["Mailpit:Path"] ?? "mailpit";
var mailpit = builder.AddExecutable("mailpit", mailpitPath, workingDirectory: ".",
        "--smtp", "127.0.0.1:1025", "--listen", "127.0.0.1:8025")
    .WithEndpoint(port: 1025, name: "smtp", scheme: "tcp", isProxied: false)
    .WithHttpEndpoint(port: 8025, name: "ui", isProxied: false)
    .WithHttpHealthCheck("/livez", endpointName: "ui");
var mailpitSmtp = mailpit.GetEndpoint("smtp");

// User-secrets placeholders (override with `dotnet user-secrets set "Parameters:sysadmin-email" ...`).
var sysAdminEmail = builder.AddParameter("sysadmin-email");
var smtpFrom = builder.AddParameter("smtp-from");

var api = builder.AddProject<Projects.EventHub_Api>("api")
    .WithReference(database)
    .WaitFor(database)
    // SMTP is reported on /health only, never readiness (AD-21), so the API does not wait for Mailpit.
    .WithEnvironment("EventHub__Smtp__Host", mailpitSmtp.Property(EndpointProperty.Host))
    .WithEnvironment("EventHub__Smtp__Port", mailpitSmtp.Property(EndpointProperty.Port))
    .WithEnvironment("EventHub__Seed__SystemAdministrators__0", sysAdminEmail)
    .WithEnvironment("EventHub__Smtp__From", smtpFrom)
    .WithHttpHealthCheck("/health/ready");

// The SPA lives in the sibling frontend repo (path is relative to this project directory).
// WithReference(api) injects services__api__https__0 / services__api__http__0 for the Vite /api proxy.
builder.AddViteApp("web", "../../../frontend")
    .WithReference(api)
    .WaitFor(api)
    .WithExternalHttpEndpoints();

builder.Build().Run();
