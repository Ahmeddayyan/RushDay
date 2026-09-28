# Runs the API locally as a framework-dependent single-file publish (docs/spec/06-implementation-plan.md section 1
# rule 5). Windows Smart App Control blocks `dotnet run` on freshly built unsigned assemblies on this machine
# (FileLoadException 0x800711C7); a published RushDay.Api.exe runs. Windows PowerShell 5.1 compatible.
#
#   scripts/run-api.ps1                              publish, then start on http://localhost:5080 (Development)
#   scripts/run-api.ps1 -Args "--migrate-and-seed"   arguments passed through to the API
#   scripts/run-api.ps1 -NoPublish                   start the last publish without publishing again
#   scripts/run-api.ps1 -PublishOnly                 publish and exit
#
# The caller's environment (ConnectionStrings__RushDay, Demo__Enabled, ...) is inherited; ASPNETCORE_ENVIRONMENT
# defaults to Development and ASPNETCORE_URLS to http://localhost:5080 unless already set. Exits with the exe's code.
[CmdletBinding()]
param(
    [Alias("Args")]
    [string[]]$ArgumentList = @(),
    [switch]$NoPublish,
    [switch]$PublishOnly
)

$ErrorActionPreference = "Stop"
$root = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$publishDir = Join-Path $env:LOCALAPPDATA "RushDay\api"
$project = Join-Path $root "src\RushDay.Api"

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    $env:PATH = "C:\Program Files\dotnet;" + $env:PATH
}

if (-not $NoPublish) {
    & dotnet publish $project -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -o $publishDir
    if ($LASTEXITCODE -ne 0) {
        Write-Error "dotnet publish failed with exit code $LASTEXITCODE."
        exit $LASTEXITCODE
    }
}

if ($PublishOnly) {
    Write-Host "Published to $publishDir."
    exit 0
}

$exe = Join-Path $publishDir "RushDay.Api.exe"
if (-not (Test-Path $exe)) {
    Write-Error "$exe not found; run without -NoPublish first."
    exit 1
}

# Set the defaults only for the child; restore the caller's shell afterwards.
$previousEnvironment = $env:ASPNETCORE_ENVIRONMENT
$previousUrls = $env:ASPNETCORE_URLS
if (-not $env:ASPNETCORE_ENVIRONMENT) { $env:ASPNETCORE_ENVIRONMENT = "Development" }
if (-not $env:ASPNETCORE_URLS) { $env:ASPNETCORE_URLS = "http://localhost:5080" }

# The publish folder is the working directory so wwwroot and appsettings*.json resolve.
Push-Location $publishDir
try {
    & $exe @ArgumentList
    $exitCode = $LASTEXITCODE
} finally {
    Pop-Location
    $env:ASPNETCORE_ENVIRONMENT = $previousEnvironment
    $env:ASPNETCORE_URLS = $previousUrls
}

exit $exitCode
