# eventhub-backend

ASP.NET Core 10 API, background workers and Aspire AppHost for EventHub.

This repo is one of three, cloned side by side under the `eventhub-docs` workspace root:

```text
eventhub/            # eventhub-docs (planning docs, git-ignores the two code repos)
  backend/           # eventhub-backend  (this repo)
  frontend/          # eventhub-frontend (React SPA; the AppHost starts it from ../../../frontend)
```

No containers are needed for local development or tests. SQL Server and Mailpit run natively on Windows.

## Prerequisites

1. **.NET SDK 10.0.401** (pinned in `global.json`) and **Node.js 24**.
2. **SQL Server 2025 Developer edition**: download the free Developer edition installer from Microsoft, choose
   *Custom*, select **Database Engine Services** and **Full-Text and Semantic Extractions for Search**, keep the
   **default instance** (`MSSQLSERVER`) and **Windows authentication**. Check with
   `sqlcmd -S localhost -E -C -Q "SELECT @@VERSION, SERVERPROPERTY('IsFullTextInstalled')"`.
   Developer edition is free for development and test only, **never for production**.
3. **Mailpit v1.31.3**: download `mailpit-windows-amd64.zip` from the axllent/mailpit GitHub release v1.31.3, unzip
   `mailpit.exe` to a folder on `PATH` (or set its full path, below). SMTP listens on 1025, the web UI on 8025.
4. Clone `eventhub-frontend` into `../frontend` and run `npm ci` there once.

## Run locally

```powershell
cd backend
dotnet run --project src/EventHub.AppHost
```

The Aspire dashboard shows the `eventhub` SQL connection (health-checked; the empty `EventHub` database is created
on first start), `mailpit` (started from `mailpit.exe`), the API and the Vite dev server (`web`). Open the `web` URL:
the browser calls `/api` on the same origin and Vite proxies it to the API. Mailpit UI: http://localhost:8025.

Optional user secrets (defaults live in `src/EventHub.AppHost/appsettings.json`):

```powershell
dotnet user-secrets --project src/EventHub.AppHost set "ConnectionStrings:eventhub" "Server=localhost;Database=EventHub;Trusted_Connection=True;TrustServerCertificate=True"
dotnet user-secrets --project src/EventHub.AppHost set "Mailpit:Path" "C:\tools\mailpit\mailpit.exe"
dotnet user-secrets --project src/EventHub.AppHost set "Parameters:sysadmin-email" "you@example.com"
dotnet user-secrets --project src/EventHub.AppHost set "Parameters:smtp-from" "no-reply@example.com"
```

These flow into the API as `ConnectionStrings__eventhub`, `EventHub__Smtp__Host/Port`,
`EventHub__Seed__SystemAdministrators__0` and `EventHub__Smtp__From`.

| Endpoint | Purpose |
| --- | --- |
| `GET /health/live` | Process up (no dependency checks) |
| `GET /health/ready` | Database reachability only |
| `GET /health` | Detail: database + SMTP |
| `GET /api/auth/antiforgery` | Issues the JS-readable `XSRF-TOKEN` cookie |
| `/scalar` | API reference (Development only) |

## Build, test, CI

```powershell
dotnet build -warnaserror      # also regenerates openapi/openapi.json (OpenAPI 3.0)
dotnet test                    # Microsoft Testing Platform; integration tests use local SQL Server
./ci.ps1                       # the single CI entry point (build, drift check, tests, vulnerable-package audit)
```

Integration tests create a fresh `EventHub_Test_<guid>` database and drop it afterwards. The server comes from
`EVENTHUB_TEST_SQL` (default `Server=localhost;Trusted_Connection=True;TrustServerCertificate=True`). If SQL Server is
unreachable, the database tests are skipped with the reason.

`openapi/openapi.json` is the only FE/BE contract. It is generated at build time and committed; never edit it by hand.
If it changes, commit it, then run `npm run api:generate` in `eventhub-frontend`.

The `Dockerfile` is kept for future hosting only; local dev and CI never build it.

## Layout

| Path | Holds |
| --- | --- |
| `src/EventHub.Domain` | Aggregates, value objects, policies (BCL only) |
| `src/EventHub.Application` | Commands, queries, handlers, ports, pipeline behaviors |
| `src/EventHub.Contracts` | Wire DTOs and enums |
| `src/EventHub.Infrastructure` | EF Core, SQL, Identity stores, SMTP, outbox, scheduler |
| `src/EventHub.Api` | Minimal API endpoints, auth, OpenAPI, composition root |
| `src/EventHub.AppHost`, `src/EventHub.ServiceDefaults` | Aspire local orchestration, OpenTelemetry and health defaults |
| `tests/` | Domain, Application (incl. architecture tests), Api integration tests |

## Licences

All packages are MIT or Apache-2.0. Mailpit is MIT. SQL Server Developer edition is free for development and test only.
