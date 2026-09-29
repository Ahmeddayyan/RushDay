<#
Runs the Playwright journeys and accessibility scans (docs/spec/05-frontend.md section 13.2) against a throwaway
database. Windows PowerShell 5.1 compatible; never uses `dotnet run` (docs/spec/06-implementation-plan.md section 1
rule 5).

  1. Drops and recreates the database (default rushday_e2e, owner rushday) with psql, found as scripts/reset-db.ps1
     finds it.
  2. npm run build in src/RushDay.Web (the SPA goes into src/RushDay.Api/wwwroot).
  3. Publishes the API: scripts/run-api.ps1 -PublishOnly, or the same single-file publish into -PublishDir.
  4. Prepares the data with two --migrate-and-seed starts of that publish: the demo seed (300 students, results day
     2026-01-26) with demo mode on, then a start with demo mode off and Bootstrap__AdminPassword set, which creates
     the one real (non-demo) administrator the account journeys need, because the demo administrator can only
     provision read-only demo accounts (docs/spec/02-api.md section 8.5). The web server's own start, with demo mode
     on again, restores the demo accounts that start disabled.
  5. npm run test:e2e in src/RushDay.Web; Playwright starts the published API through the webServer command of
     playwright.config.ts and stops it at the end. Exits with Playwright's exit code.

  scripts/e2e.ps1                                   the whole suite on http://localhost:5080
  scripts/e2e.ps1 -Port 5103 -PublishDir C:\tmp\api  beside a dev API that holds port 5080 and the shared publish
  scripts/e2e.ps1 -PlaywrightArgs "--project=desktop-chromium","login.spec.ts"

The database is left in place afterwards for inspection; the next run drops it.
#>
[CmdletBinding()]
param(
    [int]$Port = 5080,
    [string]$PublishDir,
    [string]$Database = "rushday_e2e",
    [string[]]$PlaywrightArgs = @()
)

$ErrorActionPreference = "Stop"
$root = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$web = Join-Path $root "src\RushDay.Web"

# Must match ROOT_ADMIN in src/RushDay.Web/e2e/fixtures.ts (and the CI e2e job).
$adminUsername = "e2e-admin"
$adminPassword = "Harbour-Lantern-4821"

foreach ($dir in @("C:\Program Files\dotnet", "C:\Program Files\nodejs")) {
    if ((Test-Path $dir) -and -not (($env:PATH -split ";") -contains $dir)) { $env:PATH = "$dir;" + $env:PATH }
}

function Invoke-Checked([string]$What, [scriptblock]$Command) {
    & $Command
    if ($LASTEXITCODE -ne 0) {
        Write-Error "$What failed with exit code $LASTEXITCODE."
        exit $LASTEXITCODE
    }
}

# 1. A fresh database.
$psql = Get-ChildItem "C:\Program Files\PostgreSQL\*\bin\psql.exe" -ErrorAction SilentlyContinue |
    Sort-Object FullName -Descending | Select-Object -First 1
$psqlPath = if ($psql) { $psql.FullName } else { (Get-Command psql -ErrorAction Stop).Source }
if (-not $env:PGPASSWORD) { $env:PGPASSWORD = "rushday" }
Invoke-Checked "Recreating $Database" {
    & $psqlPath -U postgres -h localhost -v ON_ERROR_STOP=1 `
        -c "DROP DATABASE IF EXISTS $Database WITH (FORCE)" `
        -c "CREATE DATABASE $Database OWNER rushday"
}

# 2. The SPA.
Push-Location $web
try {
    if (-not (Test-Path (Join-Path $web "node_modules"))) { Invoke-Checked "npm ci" { npm ci --no-audit --no-fund } }
    Invoke-Checked "npm run build" { npm run build }
} finally {
    Pop-Location
}

# 3. The API, published (never `dotnet run`).
if ($PublishDir) {
    $PublishDir = [System.IO.Path]::GetFullPath($PublishDir)
    Invoke-Checked "dotnet publish" {
        dotnet publish (Join-Path $root "src\RushDay.Api") -c Release -r win-x64 --self-contained false `
            -p:PublishSingleFile=true -o $PublishDir
    }
} else {
    Invoke-Checked "scripts/run-api.ps1 -PublishOnly" { & (Join-Path $PSScriptRoot "run-api.ps1") -PublishOnly }
}

function Invoke-Api([string[]]$Arguments) {
    if ($PublishDir) {
        # What scripts/run-api.ps1 -NoPublish does, for a publish folder of our own.
        Push-Location $PublishDir
        try { & (Join-Path $PublishDir "RushDay.Api.exe") @Arguments } finally { Pop-Location }
    } else {
        & (Join-Path $PSScriptRoot "run-api.ps1") -NoPublish -Args $Arguments
    }
}

# 4. The data: the demo seed, then the real administrator (a start with demo mode off).
$connection = "Host=localhost;Port=5432;Database=$Database;Username=rushday;Password=rushday"
$prepared = @{
    ASPNETCORE_ENVIRONMENT        = "Development"
    ConnectionStrings__RushDay    = $connection
    Database__SeedStudentCount    = "300"
    Database__SeedResultsDay      = "2026-01-26T09:00:00Z"
    Demo__Enabled                 = "true"
}
$saved = @{}
foreach ($name in @($prepared.Keys) + @("Bootstrap__AdminUsername", "Bootstrap__AdminPassword")) {
    $saved[$name] = [Environment]::GetEnvironmentVariable($name, "Process")
}
try {
    foreach ($name in $prepared.Keys) { [Environment]::SetEnvironmentVariable($name, $prepared[$name], "Process") }
    Invoke-Checked "Seeding $Database (demo mode on)" { Invoke-Api @("--migrate-and-seed") }

    $env:Demo__Enabled = "false"
    $env:Bootstrap__AdminUsername = $adminUsername
    $env:Bootstrap__AdminPassword = $adminPassword
    Invoke-Checked "Creating the e2e administrator (demo mode off)" { Invoke-Api @("--migrate-and-seed") }
} finally {
    foreach ($name in $saved.Keys) { [Environment]::SetEnvironmentVariable($name, $saved[$name], "Process") }
}

# 5. The suite. Playwright starts the API (webServer) with demo mode on and stops it afterwards.
$env:E2E_CONNECTION_STRING = $connection
$env:E2E_PORT = "$Port"
$env:E2E_ADMIN_USERNAME = $adminUsername
$env:E2E_ADMIN_PASSWORD = $adminPassword
if ($PublishDir) {
    $env:E2E_SERVER_COMMAND = "powershell -NoProfile -ExecutionPolicy Bypass -Command " +
        "`"Set-Location -LiteralPath '$PublishDir'; & '.\RushDay.Api.exe'`""
}
Push-Location $web
try {
    npm run test:e2e -- @PlaywrightArgs
    $exitCode = $LASTEXITCODE
} finally {
    Pop-Location
}
exit $exitCode
