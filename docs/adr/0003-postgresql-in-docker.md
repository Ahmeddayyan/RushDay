# 3. PostgreSQL 18 in Docker Compose

Date: 2026-09-27

## Status

Accepted

## Context

The failure modes we want to reproduce (connection pool exhaustion, lock contention on a hot row,
N+1 query storms) only appear against a real relational database with a real connection pool.
An in-memory provider would hide exactly the problems this project is about.

## Decision

Run PostgreSQL 18 locally via `docker-compose.yml`. Integration tests use Testcontainers to spin up
the same image, so tests and load runs hit identical database behaviour. Schema is managed by EF Core
migrations. Development credentials live in `appsettings.Development.json` and are not secrets.

## Consequences

- Docker Desktop (and WSL2 on Windows Home) is a prerequisite for running anything beyond unit tests.
- CI runs integration tests on Linux runners where Docker is available.

## Update, 2026-09-29 (stage S11): a native PostgreSQL install is an equal local alternative

Docker turned out not to be available at all on the machine v1 is built on (a firmware restriction blocks it, not a
missed install step), so this decision needed a genuine alternative rather than a workaround. A native PostgreSQL 18
install on `localhost:5432` is now an equal, first-class option for local work, not a lesser fallback: when the
environment variable `RUSHDAY_TEST_CONNECTION` is set (D23), the integration test factory connects to the native
server's `postgres` maintenance database with the same role, creates and drops a throwaway database for the run
itself, and runs the exact same test suite against it as it does against the Testcontainers-provisioned image.
Nothing in the tests knows or cares which one it is talking to. CI continues to use Testcontainers on its Linux
runners, where Docker is available and reproducing the exact image PostgreSQL version is straightforward; a
contributor without Docker runs the same suite locally against native PostgreSQL with no code path forked for
either case. `scripts/db-create.ps1` provisions the native role and database, and grants it `CREATEDB` so the test
factory's own create/drop cycle works without further setup.
