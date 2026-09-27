# Start PostgreSQL and wait until it is accepting connections.
$ErrorActionPreference = "Stop"
Set-Location (Join-Path $PSScriptRoot "..")
docker compose up -d --wait
