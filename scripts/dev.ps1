# Runs the API and the Vite dev server together. Never uses `dotnet run` or `dotnet watch run`:
# Windows Smart App Control blocks both on this machine (06-implementation-plan.md section 1 rule 5),
# so the API always runs through scripts/run-api.ps1's published exe. Re-run this script after a
# backend change; the published exe is not watched.
[CmdletBinding()]
param()

$ErrorActionPreference = "Stop"
$root = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$webDir = Join-Path $root "src\RushDay.Web"

if (-not (Get-Command npm -ErrorAction SilentlyContinue)) {
    $env:PATH = "C:\Program Files\nodejs;" + $env:PATH
}

Write-Host "Starting the API (scripts/run-api.ps1) in a new window..."
Start-Process powershell -ArgumentList @(
    "-NoExit", "-NoProfile", "-ExecutionPolicy", "Bypass",
    "-File", (Join-Path $PSScriptRoot "run-api.ps1")
)

Write-Host "Starting the Vite dev server (npm run dev) in this window..."
Write-Host "http://localhost:5173"
Push-Location $webDir
try {
    & npm run dev
} finally {
    Pop-Location
}
