# Apply migrations and seed the deterministic 20,000-student dataset (no-op if already seeded).
$ErrorActionPreference = "Stop"
Set-Location (Join-Path $PSScriptRoot "..")
dotnet run --project src/RushDay.Api -- --migrate-and-seed
