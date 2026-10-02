<#
Runs one k6 scenario against a RushDay API and keeps its summary (04-performance-and-ops.md sections 8-9).
Starts the API itself, publishing and launching it the same way scripts/run-api.ps1 does (never `dotnet run`),
unless -BaseUrl or -NoStart says an API is already running (your own scripts/run-api.ps1 in another window, or a
scratch instance on a different port and database).

  scripts/load.ps1 results-day
  scripts/load.ps1 enrolment-rush -Rushers 500
  scripts/load.ps1 dashboard-knee -Rate 2000
  scripts/load.ps1 login-storm -Mode guard -ProductionLoginGuard
  scripts/load.ps1 login-storm -Mode spray
  scripts/load.ps1 results-day -BaseUrl http://localhost:5101   # target an API you started yourself
  scripts/load.ps1 dashboard-knee -Rate 1000 -NoStart           # target http://localhost:5080, already running

-ProductionLoginGuard restarts the API with only the PBKDF2 concurrency guard at production strength
(RateLimiting__LoginConcurrency=8, RateLimiting__LoginQueue=16) and every other login limiter opened up to
100000/minute, per 04-performance-and-ops.md section 9 step 5, so the guard's own throughput and 429 behaviour is
what the run measures, not the per-user or per-IP windows. -Mode spray always sets
RateLimiting__LoginFailuresPerIpPer10Minutes=20 (the production default) regardless of -ProductionLoginGuard, because
the spray's story is that window's onset.

Reset the database between enrolment-rush runs with scripts/reset-db.ps1 for a clean 30 places; in demo mode
CS3099's self-enrolments are also withdrawn on every API start (01-domain-and-data.md section 6, step 11), so a
plain restart already gives you most of the way there.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true, Position = 0)]
    [ValidateSet("results-day", "enrolment-rush", "dashboard-knee", "login-storm")]
    [string]$Scenario,

    [int]$Rate = 1000,
    [int]$Rushers = 500,
    [int]$LoginPool = 200,
    [int]$StudentCount = 20000,
    [string]$ModuleCode = "CS3099",

    [ValidateSet("guard", "spray")]
    [string]$Mode = "guard",
    [switch]$ProductionLoginGuard,

    # Target an API that is already running; skips publishing and starting one of our own.
    [string]$BaseUrl,
    [switch]$NoStart
)

$ErrorActionPreference = "Stop"
$root = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
Set-Location $root

if (-not (Get-Command k6 -ErrorAction SilentlyContinue)) {
    $env:PATH = "C:\Program Files\k6;" + $env:PATH
}
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    $env:PATH = "C:\Program Files\dotnet;" + $env:PATH
}

New-Item -ItemType Directory -Force -Path load/results | Out-Null

$targetUrl = if ($BaseUrl) { $BaseUrl.TrimEnd('/') } else { "http://localhost:5080" }
$startedProcess = $null
$startOwnApi = -not $BaseUrl -and -not $NoStart

if ($startOwnApi) {
    Write-Host "Publishing the API (scripts/run-api.ps1 -PublishOnly)..."
    & (Join-Path $PSScriptRoot "run-api.ps1") -PublishOnly
    if ($LASTEXITCODE -ne 0) {
        Write-Error "Publish failed with exit code $LASTEXITCODE."
        exit $LASTEXITCODE
    }

    $publishDir = Join-Path $env:LOCALAPPDATA "RushDay\api"
    $exe = Join-Path $publishDir "RushDay.Api.exe"
    if (-not (Test-Path $exe)) {
        Write-Error "$exe not found after publish."
        exit 1
    }

    # scripts/run-api.ps1 itself blocks in the foreground; a load run needs k6 running at the same time, so this
    # script launches the same published exe under the same environment contract (ASPNETCORE_ENVIRONMENT,
    # ASPNETCORE_URLS) and keeps the Process handle to stop it cleanly afterwards.
    $previousEnvironment = $env:ASPNETCORE_ENVIRONMENT
    $previousUrls = $env:ASPNETCORE_URLS
    if (-not $env:ASPNETCORE_ENVIRONMENT) { $env:ASPNETCORE_ENVIRONMENT = "Development" }
    if (-not $env:ASPNETCORE_URLS) { $env:ASPNETCORE_URLS = $targetUrl }

    $guardVars = @()
    if ($Scenario -eq "login-storm" -and $ProductionLoginGuard) {
        Write-Host "-ProductionLoginGuard: only the CPU guard at production strength (LoginConcurrency=8, LoginQueue=16); every other login limiter opened up."
        $env:RateLimiting__LoginConcurrency = "8"
        $env:RateLimiting__LoginQueue = "16"
        $env:RateLimiting__LoginPerIpPerMinute = "100000"
        $env:RateLimiting__LoginPerUserPerMinute = "100000"
        $env:RateLimiting__LoginFailuresPerIpPer10Minutes = "100000"
        $guardVars += "RateLimiting__LoginConcurrency", "RateLimiting__LoginQueue", "RateLimiting__LoginPerIpPerMinute", "RateLimiting__LoginPerUserPerMinute", "RateLimiting__LoginFailuresPerIpPer10Minutes"
    }
    if ($Scenario -eq "login-storm" -and $Mode -eq "spray") {
        Write-Host "-Mode spray: RateLimiting__LoginFailuresPerIpPer10Minutes=20 (production default)."
        $env:RateLimiting__LoginFailuresPerIpPer10Minutes = "20"
        $guardVars += "RateLimiting__LoginFailuresPerIpPer10Minutes"
    }

    try {
        $startedProcess = Start-Process -FilePath $exe -WorkingDirectory $publishDir -PassThru -WindowStyle Hidden
    } finally {
        $env:ASPNETCORE_ENVIRONMENT = $previousEnvironment
        $env:ASPNETCORE_URLS = $previousUrls
        foreach ($name in ($guardVars | Select-Object -Unique)) {
            Remove-Item "Env:$name" -ErrorAction SilentlyContinue
        }
    }

    Write-Host "Waiting for $targetUrl/api/health/live ..."
    $ready = $false
    for ($i = 0; $i -lt 60; $i++) {
        try {
            $response = Invoke-WebRequest -Uri "$targetUrl/api/health/live" -UseBasicParsing -TimeoutSec 2
            if ($response.StatusCode -eq 200) { $ready = $true; break }
        } catch {
            Start-Sleep -Milliseconds 500
        }
    }
    if (-not $ready) {
        if ($startedProcess) { Stop-Process -Id $startedProcess.Id -Force -ErrorAction SilentlyContinue }
        Write-Error "API did not answer $targetUrl/api/health/live within about 30s."
        exit 1
    }
    Write-Host "API is up at $targetUrl."
}

$k6ExitCode = 1
try {
    $stamp = Get-Date -Format "yyyyMMdd-HHmmss"
    $name = $Scenario
    $k6Args = @("-e", "BASE_URL=$targetUrl", "-e", "STUDENT_COUNT=$StudentCount")

    switch ($Scenario) {
        "results-day" {
            $k6Args += @("-e", "LOGIN_POOL=$LoginPool")
        }
        "enrolment-rush" {
            $k6Args += @("-e", "RUSHERS=$Rushers", "-e", "MODULE_CODE=$ModuleCode")
        }
        "dashboard-knee" {
            $name = "$Scenario-${Rate}rps"
            $k6Args += @("-e", "RATE=$Rate", "-e", "LOGIN_POOL=$LoginPool")
        }
        "login-storm" {
            $name = "$Scenario-$Mode"
            $k6Args += @("-e", "MODE=$Mode")
        }
    }

    $summaryPath = "load/results/$name-$stamp.json"
    Write-Host "k6 run load/k6/$Scenario.js -> $summaryPath (BASE_URL=$targetUrl)"
    & k6 run @k6Args "load/k6/$Scenario.js" --summary-export $summaryPath
    $k6ExitCode = $LASTEXITCODE
} finally {
    if ($startedProcess) {
        Write-Host "Stopping the API (PID $($startedProcess.Id))..."
        Stop-Process -Id $startedProcess.Id -Force -ErrorAction SilentlyContinue
    }
}

exit $k6ExitCode
