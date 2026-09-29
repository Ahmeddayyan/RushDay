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

## Update, 2026-09-29 (stage S11): deploy gating, no demo values in the Blueprint, and a non-owner application role

Three things changed since this ADR was first written, all in the direction of "a customer could deploy this the
same way and not inherit any of RushDay's own demo setup":

- **Render deploys on `checksPass`, not on every push.** `render.yaml` sets `autoDeployTrigger: checksPass`: a
  deploy only happens once GitHub's required checks (`web`, `build-and-test`, and from stage S12 onward `e2e`) have
  passed on the commit, so a red build on `main` never reaches the live URL. This replaced an earlier plan to gate
  deploys with a manual deploy-hook call from the CI workflow; `checksPass` is Render's own built-in equivalent and
  needs no secret deploy-hook URL to manage.
- **The Blueprint (`render.yaml`) carries no demo or seeding values at all** (D29): no `Demo__*` key and no
  `Database__Seed*` key appears in the committed file. The public demo's `Demo__Enabled=true` and
  `Demo__PublicDemoAcknowledged=true` are set only in the Render dashboard, by hand, for this one deployment. A
  university applying the same Blueprint to their own Render account gets a customer-shaped deployment by default —
  no synthetic accounts, no published passwords — not a copy of RushDay's own demo that they would have to
  remember to turn off.
- **The application does not connect as the database owner.** The running API and the startup backfills connect as
  `rushday_app`, a role granted `SELECT`/`INSERT`/`UPDATE`/`DELETE` on the existing tables and `USAGE`/`SELECT` on
  sequences, but neither `TRUNCATE` nor any DDL privilege (`docs/spec/03-security.md` section 8 has the exact grant
  script). Withholding `TRUNCATE` specifically is what keeps the `audit_events` append-only trigger binding on this
  role: a row-level trigger does not fire on `TRUNCATE`, so a role that could truncate the table could erase the
  audit trail without ever violating the trigger. Migrations run separately, on `ConnectionStrings:Migrations`
  (the Neon owner role) when that variable is set; `docs/deployment.md` records the residual risk of the simpler
  one-role setup for a customer who chooses not to configure a separate migrations connection.
