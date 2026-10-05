#!/usr/bin/env pwsh
# Single CI entry point for eventhub-backend (CI provider wiring is deferred).
# Runs: restore, Release build (regenerates openapi/openapi.json), contract drift check, Debug build, all tests
# from the Debug build (unit, architecture, integration against a throwaway database on local SQL Server; the
# seed-only test tooling exists only outside Release, and a test checks the Release assemblies lack it),
# vulnerable-package audit.
# Requires: .NET SDK 10.0.401, git, local SQL Server 2025 Developer (or EVENTHUB_TEST_SQL). No containers.
# Database tests fail (not skip) when SQL Server is unreachable; pass -AllowSqlSkip to let them skip.
#Requires -Version 5.1
param(
    [switch]$AllowSqlSkip
)

$ErrorActionPreference = 'Stop'
Set-Location -Path $PSScriptRoot

if ($AllowSqlSkip) { Remove-Item Env:EVENTHUB_REQUIRE_SQL -ErrorAction SilentlyContinue } else { $env:EVENTHUB_REQUIRE_SQL = '1' }
# SeedToolsReleaseTests reads the Release output built below; fail (not skip) if it is missing.
$env:EVENTHUB_REQUIRE_RELEASE_BUILD = '1'

function Invoke-Step {
    param([string]$Name, [scriptblock]$Action)
    Write-Host ""
    Write-Host "==> $Name" -ForegroundColor Cyan
    & $Action
    if ($LASTEXITCODE -ne 0) {
        Write-Host "FAILED: $Name (exit $LASTEXITCODE)" -ForegroundColor Red
        exit $LASTEXITCODE
    }
}

Invoke-Step 'restore' { dotnet restore EventHub.slnx }

Invoke-Step 'build Release (-warnaserror, regenerates openapi.json)' {
    dotnet build EventHub.slnx --no-restore -c Release -warnaserror
}

Invoke-Step 'openapi.json drift check' {
    git ls-files --error-unmatch openapi/openapi.json *> $null
    if ($LASTEXITCODE -ne 0) {
        Write-Host 'openapi/openapi.json is not tracked by git. Commit the generated contract.' -ForegroundColor Red
        exit 1
    }
    # Compare against the last commit when one exists; a brand-new repo falls back to the index.
    git rev-parse --verify --quiet HEAD *> $null
    if ($LASTEXITCODE -eq 0) { git diff --exit-code --stat HEAD -- openapi/ } else { git diff --exit-code --stat -- openapi/ }
    if ($LASTEXITCODE -ne 0) {
        Write-Host 'openapi/openapi.json differs from the committed contract. Commit the regenerated file (never hand-edit it).' -ForegroundColor Red
    }
}

Invoke-Step 'build Debug (-warnaserror, EVENTHUB_SEED_TOOLS defined)' {
    dotnet build EventHub.slnx --no-restore -c Debug -warnaserror
}

Invoke-Step 'test (unit, architecture, integration) from the Debug build' {
    dotnet test --solution EventHub.slnx --no-build -c Debug
}

Invoke-Step 'vulnerable package audit' {
    $report = dotnet list EventHub.slnx package --vulnerable --include-transitive 2>&1 | Out-String
    Write-Host $report
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    if ($report -match 'has the following vulnerable packages') {
        Write-Host 'Vulnerable packages found.' -ForegroundColor Red
        exit 1
    }
}

Write-Host ""
Write-Host 'CI passed.' -ForegroundColor Green
exit 0
