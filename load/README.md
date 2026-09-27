# Load tests

Two k6 scenarios, one per failure story.

| Script | Story | What to watch |
|---|---|---|
| `k6/results-day.js` | 09:00, marks published, whole cohort opens the dashboard | `http_req_failed`, p95/p99 latency, when errors start |
| `k6/enrolment-rush.js` | 500 students race for 30 places on one module | `OVERSOLD=` line in teardown |
| `k6/dashboard-knee.js` | Hold the dashboard at a fixed rate (`-e RATE=2000`) for 30s | where p95 climbs and `dropped_iterations` appears |

## Run

```powershell
.\scripts\seed.ps1                       # once: migrate + seed 20,000 students
dotnet run --project src\RushDay.Api -c Release   # separate terminal
.\scripts\load.ps1 results-day
.\scripts\load.ps1 enrolment-rush
.\scripts\reset-db.ps1                   # between enrolment-rush runs
```

Each run writes a JSON summary to `load/results/` so before and after numbers can be compared and quoted.
Run the API in Release when measuring; Debug builds are noticeably slower.

Override the target with `BASE_URL`, e.g. `$env:BASE_URL="http://localhost:8080"`. Do not point these at
the free-tier deployment for measurements; it is shared hardware and the numbers mean nothing.
