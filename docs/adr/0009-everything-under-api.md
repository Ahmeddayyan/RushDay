# 9. Every route under `/api`; removal of the unauthenticated v0 routes and the interim page

Date: 2026-09-29

## Status

Accepted

## Context

v0 exposed `GET /students/{n}/dashboard`, `GET /modules`, `GET /modules/{code}` and
`POST /students/{n}/enrolments` at the site root, alongside a working-tree-only `wwwroot/index.html` that could read
any student's marks with no login (D21). Both were fine for a solo load-testing exercise against synthetic data and
indefensible for anything that looks like a product: a buyer's very first question about a portal that shows marks
is "can a student read someone else's?", and v0's honest answer was yes, trivially, by changing a number in the URL.

Once real authentication exists (ADR 7), routes also need a place to live that is unambiguous about what is an API
call versus what is the SPA's own client-side routing, since both are served from the same origin and the same port.

## Decision

- Every API route lives under `/api` (D25): `/api/auth`, `/api/public`, `/api/health`, `/api/me` (the signed-in
  student's own data — no student number in the path, ever), `/api/modules`, `/api/announcements`,
  `/api/lecturer`, `/api/admin`. Anything under `/api/{**rest}` that does not match a mapped route is
  `AllowAnonymous` and answers a JSON 404, so probing for a route never leaks whether it exists behind a role check
  it would otherwise fail.
- Because no student-role route takes a student number, "can `S000001` read `S000002`'s data" is not a runtime
  check that could be forgotten on a new endpoint — it is structurally impossible: there is no parameter to put the
  wrong value in. The integration test suite still asserts it directly (`S000001`'s session against
  `/api/admin/students/S000002` → 403) because a structural guarantee is worth confirming, not because it is in
  doubt.
- The legacy `/students/*`, `/modules` (root-level) and `/health` routes, and the working-tree-only interim
  `wwwroot/index.html`, are deleted outright rather than kept as deprecated aliases. A deprecated-but-reachable
  unauthenticated route to a marks endpoint is not a deprecation, it is a vulnerability with a changelog entry.
- Everything else (`/`, deep SPA links like `/student/results`, static assets) is served by the same container from
  `wwwroot`, with a SPA fallback for any non-`/api` path so client-side routing works on a hard refresh; requests
  outside `/api` that would 404 in a conventional API instead render the SPA shell, which then renders its own
  not-found page.

## Consequences

- `POST /api/auth/register` answering 404 (not a route the API exposes at all — `MapIdentityApi` is never
  referenced) is a permanent acceptance-checklist item, not a one-time check, because "no self-registration" is a
  product decision (the university provisions every account) that a future change could otherwise reintroduce by
  accident.
- k6 and Playwright both walk the same `/api/...` surface a browser does; there is no separate "test-only" route
  left over from v0 to keep in sync or to forget to remove before release.
- The optional `hotfix/v0-writes` branch (`06-implementation-plan.md` stage S0) is the stop-gap for the live v0
  deployment between this decision being written and v1 actually shipping: it makes v0's write route 404 unless a
  v0-only, never-set flag is present, and is designed to be thrown away (both files it touches are already owned by
  v1's `main`-merge conflict resolution) the moment `v1` merges.

**v0 evidence:** the interim page and the bare routes are the finding itself, not a load number — see
`docs/load-results/2026-09-27-v0-baseline.md` for what they made possible under load, and D21 for why the interim
page existed at all.

**v1 evidence:** `tests/RushDay.IntegrationTests/Auth/AuthorizationMatrixTests.cs` and the acceptance checklist's
"Identity and access" section (`docs/spec/00-overview.md` section 8) are the ongoing evidence for this decision —
there is no load number to record here; the next dated load-results document (stage S13) does not add a line for
this ADR.
