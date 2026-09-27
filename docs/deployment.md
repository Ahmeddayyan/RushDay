# Deployment

Public URL: Render free web service + Neon free PostgreSQL. Deploys are triggered by GitHub Actions
after tests pass. One-time setup, about 15 minutes.

## 1. Neon (database)

1. Sign up at https://neon.tech (GitHub login works, no card).
2. Create a project named `rushday`, region **Europe (Frankfurt)**, Postgres 17 or 18.
3. Open **Connect**, choose **.NET** as the connection type, copy the connection string. It looks like:
   `Host=ep-xxxx.eu-central-1.aws.neon.tech;Database=neondb;Username=neondb_owner;Password=...;SSL Mode=Require;Channel Binding=Require`

## 2. Render (API)

1. Sign up at https://render.com (GitHub login, no card).
2. **New > Blueprint**, pick the `RushDay` repository. Render reads `render.yaml`.
3. When prompted for `ConnectionStrings__RushDay`, paste the Neon string. Apply.
4. The first deploy runs migrations and seeds 20,000 students; allow a couple of minutes.
5. Your URL is `https://rushday-api.onrender.com` (or whatever Render assigns). Try `/`, `/health`,
   `/students/S000001/dashboard`.

## 3. CI/CD hook

1. In the Render service: **Settings > Deploy Hook > Create**. Copy the URL.
2. In GitHub: repository **Settings > Secrets and variables > Actions > New repository secret**,
   name `RENDER_DEPLOY_HOOK_URL`, value the hook URL.
3. From now on every push to `main` runs the workflow in `.github/workflows/ci.yml`: build, tests,
   Docker build, then deploy. A failing test blocks the deploy.

## Resetting the cloud database

Seeding is skipped when students already exist. To reseed, run this in the Neon SQL editor and redeploy:

```sql
DROP SCHEMA public CASCADE; CREATE SCHEMA public;
```
