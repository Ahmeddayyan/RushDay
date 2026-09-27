# For a native PostgreSQL install: create the rushday role and database (idempotent).
# Assumes the postgres superuser password is in $env:PGPASSWORD (defaults to "rushday").
$ErrorActionPreference = "Stop"
$psql = Get-ChildItem "C:\Program Files\PostgreSQL\*\bin\psql.exe" | Sort-Object FullName -Descending | Select-Object -First 1
if (-not $psql) { throw "psql.exe not found under C:\Program Files\PostgreSQL. Install PostgreSQL or use scripts\db-up.ps1 with Docker." }
if (-not $env:PGPASSWORD) { $env:PGPASSWORD = "rushday" }

& $psql.FullName -U postgres -h localhost -v ON_ERROR_STOP=1 -c "DO `$`$ BEGIN IF NOT EXISTS (SELECT FROM pg_roles WHERE rolname = 'rushday') THEN CREATE ROLE rushday LOGIN PASSWORD 'rushday'; END IF; END `$`$;"
$exists = & $psql.FullName -U postgres -h localhost -tAc "SELECT 1 FROM pg_database WHERE datname = 'rushday'"
if ($exists -ne "1") { & $psql.FullName -U postgres -h localhost -v ON_ERROR_STOP=1 -c "CREATE DATABASE rushday OWNER rushday" }
Write-Host "Database rushday ready on localhost:5432 (user rushday)."
