# 6. Free hosting: Render web service + Neon PostgreSQL, deployed by CI

Date: 2026-09-27

## Status

Accepted

## Context

The project should have a public URL from day one so it can sit on a CV, at zero cost and without a
credit card. It also needs a database that survives more than a trial period.

## Decision

- **API**: Render free web service, built from the repository Dockerfile (`render.yaml`).
- **Database**: Neon free PostgreSQL. Connection string is a Render environment variable, never in git.
- **Pipeline**: GitHub Actions builds, runs unit and integration tests, and proves the Docker image
  builds. Only then does it call a Render deploy hook. Render auto-deploy is switched off so nothing
  reaches production without passing tests.
- The app applies migrations and seeds on startup in production, because the free tier has no shell.

## Consequences

- Free tier spins down when idle; the first request after a pause takes 30 to 60 seconds.
- Free tier is shared CPU and 512 MB. Load-test numbers quoted in this repo come from a local run
  against local PostgreSQL, not from the free host. The URL is for demonstration, not benchmarking.
- Neon suspends compute when idle; the first query after a pause adds a few hundred milliseconds.
