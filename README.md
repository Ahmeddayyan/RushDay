# RushDay

**A university student portal, built to fall over on results day, then fixed one measured step at a time.**

Every September the same thing happens: marks are published at 09:00, twenty thousand students open the
dashboard in the same minute, and the portal collapses. RushDay reproduces that collapse on a laptop,
measures it, and fixes it, with every decision and every number written down.

Live demo: **https://rushday-api.onrender.com** (free tier, the
first request after idle can take up to a minute).

## The story

1. **Build the baseline (v0).** A clean, straightforward ASP.NET Core API with the code a first pass usually
   produces: one query per module on the dashboard, check-then-write enrolment, no caching.
2. **Break it.** k6 scenarios replay results day and an enrolment rush. Measure what actually breaks,
   which is not always what you predicted.
3. **Fix it, with evidence.** One failure mode at a time. Each fix gets an ADR and a before/after load run.

## Findings so far

Day one, v0 baseline, full write-up in [docs/load-results/](docs/load-results/):

- **Enrolment rush**: 500 students hit a 30-place module at once. 154 got in. The database ran out of
  connection slots and the server refused half the TCP connections.
- **Results day**: the dashboard everyone predicted would fall over first handled 800 requests per second
  with a p95 of 6.5 ms and zero errors. The N+1 query pattern is cheap when the database is on the same
  machine. Finding its real breaking point is the next experiment.

## Stack

| Concern | Choice |
|---|---|
| Runtime | .NET 10, ASP.NET Core minimal APIs |
| Data | PostgreSQL 18, EF Core 10, Npgsql |
| Tests | xUnit, Testcontainers for integration tests |
| Load | Grafana k6 |
| CI/CD | GitHub Actions, then Render deploy hook |
| Hosting | Render (API, Docker) + Neon (PostgreSQL), both free tiers |

## Run it locally

Prerequisites: .NET 10 SDK, k6, and a PostgreSQL 18 reachable at `localhost:5432` by one of:

- **Native install** (no Docker needed): install PostgreSQL, then `.\scripts\db-create.ps1` creates the
  `rushday` role and database.
- **Docker**: `.\scripts\db-up.ps1` starts the container from `docker-compose.yml`.

Then:

```powershell
.\scripts\seed.ps1                                   # migrate + seed 20,000 students (skips if seeded)
dotnet run --project src\RushDay.Api -c Release      # http://localhost:5080
```

Open `src/RushDay.Api/RushDay.Api.http` or try:

```
GET  /                                   index with links
GET  /students/S000001/dashboard
GET  /modules
GET  /modules/CS3099                     30 places, everyone wants it
POST /students/S000001/enrolments        { "moduleCode": "CS3099" }
```

## Load test it

```powershell
.\scripts\load.ps1 results-day
.\scripts\load.ps1 enrolment-rush
.\scripts\load.ps1 dashboard-knee -Rate 2000
.\scripts\reset-db.ps1                   # wipe and reseed between enrolment runs
```

See [load/README.md](load/README.md). Summaries land in `load/results/` and are committed as evidence.

## Tests

```powershell
dotnet test tests\RushDay.UnitTests
dotnet test tests\RushDay.IntegrationTests   # needs Docker (Testcontainers); CI runs these on Linux
```

## Layout

```
src/RushDay.Domain          entities and pure rules (no I/O)
src/RushDay.Infrastructure  EF Core, migrations, deterministic seeder
src/RushDay.Api             endpoints and contracts
tests/                      unit + integration
load/k6                     load scenarios; load/results holds run summaries
docs/adr                    architecture decision records
docs/load-results           what each load run showed, with numbers
docs/deployment.md          Render + Neon setup and the CI/CD hook
scripts/                    db-create, db-up, seed, reset-db, load
```

## Decisions

Start with [docs/adr](docs/adr/). ADR 5 explains why v0 is deliberately naive; ADR 6 covers hosting.
