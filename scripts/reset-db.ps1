# Wipe the database and reseed. Works with Docker (compose) or a native PostgreSQL install.
$ErrorActionPreference = "Stop"
Set-Location (Join-Path $PSScriptRoot "..")

$usingDocker = (Get-Command docker -ErrorAction SilentlyContinue) -and ((docker ps --filter "name=rushday-postgres" --format "{{.Names}}" 2>$null) -eq "rushday-postgres")
if ($usingDocker) {
    docker compose down -v
    docker compose up -d --wait
} else {
    $psql = Get-ChildItem "C:\Program Files\PostgreSQL\*\bin\psql.exe" | Sort-Object FullName -Descending | Select-Object -First 1
    if (-not $env:PGPASSWORD) { $env:PGPASSWORD = "rushday" }
    & $psql.FullName -U postgres -h localhost -v ON_ERROR_STOP=1 -c "DROP DATABASE IF EXISTS rushday WITH (FORCE)"
    & $psql.FullName -U postgres -h localhost -v ON_ERROR_STOP=1 -c "CREATE DATABASE rushday OWNER rushday"
}

scripts/run-api.ps1 -Args "--migrate-and-seed"
