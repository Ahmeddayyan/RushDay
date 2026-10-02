# Apply migrations and seed the deterministic 20,000-student dataset (no-op if already seeded).
$ErrorActionPreference = "Stop"
Set-Location (Join-Path $PSScriptRoot "..")
scripts/run-api.ps1 -Args "--migrate-and-seed"
