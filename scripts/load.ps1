# Run a k6 scenario and keep its summary.
#   .\scripts\load.ps1 results-day
#   .\scripts\load.ps1 enrolment-rush
#   .\scripts\load.ps1 dashboard-knee -Rate 2000
param(
    [Parameter(Mandatory = $true)]
    [ValidateSet("results-day", "enrolment-rush", "dashboard-knee")]
    [string]$Scenario,

    [int]$Rate = 1000
)
$ErrorActionPreference = "Stop"
Set-Location (Join-Path $PSScriptRoot "..")
$stamp = Get-Date -Format "yyyyMMdd-HHmmss"
New-Item -ItemType Directory -Force -Path load/results | Out-Null

$name = $Scenario
$extra = @()
if ($Scenario -eq "dashboard-knee") {
    $name = "$Scenario-${Rate}rps"
    $extra = @("-e", "RATE=$Rate")
}

k6 run @extra "load/k6/$Scenario.js" --summary-export "load/results/$name-$stamp.json"
