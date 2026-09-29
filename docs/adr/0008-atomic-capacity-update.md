# 8. Atomic conditional update for module capacity

Date: 2026-09-29

## Status

Accepted

## Context

`docs/load-results/2026-09-27-v0-baseline.md`, run 1: 500 students hit a 30-place module (`CS3099`) at the same
second. v0 read the current enrolled count, decided in application code whether there was room, and then inserted
the enrolment row — a classic check-then-act race with no isolation between the two steps. The result was 154
accepted enrolments against 30 places (124 oversold, 69% of all requests failed one way or another) plus a second,
independent failure (Postgres error 53300, connection-pool exhaustion under the resulting retry storm — ADR 10) and
a third (TCP refusals at the OS level — see `load/README.md`, "A Windows limit worth knowing about", and the S4
review's observation that this is a client-edition listen-backlog effect, not a server bug). This ADR is about the
first failure only: the lost update.

The options were an application-level lock (a distributed lock or a `SELECT ... FOR UPDATE` held across the whole
decision), a serializable transaction with retry-on-conflict, or pushing the check and the write into one
statement the database itself can make atomic.

## Decision

One SQL statement does the entire claim:

```sql
UPDATE modules SET enrolled_count = enrolled_count + 1
WHERE id = @moduleId AND enrolled_count < capacity AND is_active
RETURNING enrolled_count;
```

run last inside the enrolment transaction (after the student and window checks, so a doomed enrolment never reaches
the hot row), with only a `CHECK (enrolled_count >= 0)` constraint on the column — deliberately **not**
`enrolled_count <= capacity`, because a `NOT VALID` check still validates every row it touches going forward, and
the live database inherited an already-oversold `CS3099` from v0 (154 active rows for 30 places); such a constraint
would have refused every future withdrawal from that module until it was manually fixed. The claim itself makes the
invariant impossible to violate going forward; a startup reconciliation step and an admin "trim to capacity" action
(`04-performance-and-ops.md` section 2.3, `01-domain-and-data.md` section 6 step 13) catch and report the inherited
drift instead of a constraint that would block ordinary use.

Zero rows affected means "no place" — the application never needs to re-read the count, because Postgres's row-level
locking during the `UPDATE` already serialises every concurrent claim on that row; this is what makes the fix a
single round trip rather than a lock-then-check-then-write with extra latency for every enrolment, contested or not.

## Consequences

- Correctness no longer depends on the database's isolation level being anything higher than the Postgres default
  (`READ COMMITTED`); the `UPDATE ... WHERE` predicate is what removes the race, not a serializable transaction with
  its retry overhead.
- A student never gets a slow, ambiguous answer under contention: the claim is fast because it is one indexed-row
  update, not a lock held across a round trip to application code and back.
- The review that hardened `EnrolmentService.cs` before this ADR was written found and fixed a related deadlock
  hazard in the same code path (an admin's forced enrolment took `FOR UPDATE` where `FOR NO KEY UPDATE` was
  correct, so two forced claims on one module could deadlock each other; see the S4 concurrency review) — that fix
  belongs to this same statement and is exercised by `EnrolmentConcurrencyTests`.
- The integration test `EnrolmentConcurrencyTests` fires 200 real, authenticated, parallel `POST /api/me/enrolments`
  at a fresh 30-place module and asserts exactly 30 × 201, 170 × 409 `module-full`, and that
  `enrolled_count = COUNT(*) FROM enrolments WHERE status = 'Active'` for that module — the same property this ADR
  claims, checked on every CI run, not just once in a load test.

**v0 evidence:** `docs/load-results/2026-09-27-v0-baseline.md` run 1 (154 accepted / 30 places, 124 oversold, 69%
`http_req_failed`); raw summary `load/results/enrolment-rush-20260927-201228.json`.

**v1 evidence (stage S13):** `load/k6/enrolment-rush.js` run against v1 — target: `accepted=30`, `OVERSOLD=0`,
`enrolments_rejected_full=470`, `enrolments_shed=0`, zero `53300` in the API log, repeated once to show
repeatability (`04-performance-and-ops.md` section 9 steps 1-2). Not yet recorded; filled in by
`docs/load-results/2026-10-xx-v1-hardened.md` and `load/results/enrolment-rush-*.json`.
