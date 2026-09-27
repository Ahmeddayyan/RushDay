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
