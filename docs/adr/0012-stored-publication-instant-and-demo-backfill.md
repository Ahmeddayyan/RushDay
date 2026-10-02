# 12. Results publication as a stored instant; set-based demo account backfill with a shared hash and the demo-off disable

Date: 2026-09-29

## Status

Accepted

## Context

Results day is the product's central moment ("marks are published at 09:00, everyone opens the dashboard in the
same minute"), and it needs to happen reliably on a free-tier container that Render spins down when idle. A
background scheduler (a hosted timer, a cron job, `IHostedService` with a `Timer`) is a natural first instinct and
the wrong one here: a timer on a process that is not running when 09:00 arrives simply does not fire, and there is
no paid always-on tier to fall back to.

Separately, the Neon database ships already seeded with 20,000 students and 40 lecturers (`01-domain-and-data.md`),
and the load scenarios need to authenticate as many distinct students, not the same handful, or the dashboard
measurement collapses onto one hot cache line and stops measuring what a real cohort of arrivals looks like. Hashing
20,000 distinct passwords with PBKDF2 at 210,000 iterations on a 0.1 vCPU container would take hours the first time
the demo boots, which is not an acceptable startup cost.

## Decision

- **A stored instant, evaluated by reads** (D9): `results_publications.publish_at` and `grades.published_at` are
  ordinary timestamps; the visibility rule a student's read goes through is one query filter,
  `status = 'Published' AND published_at <= now() AND the enrolment is Active` — no scheduler, no background job.
  `publish_at` may be up to 90 days in the future (a genuinely scheduled results day) or in the past (an immediate
  publish); while it is in the future the publication is **scheduled** and can be rescheduled or cancelled, and a
  module can still be pulled back to draft. This is deterministic, trivially testable with a fake clock (advance the
  clock past the instant, read again), and survives every restart and every idle spin-down without needing to "catch
  up" on a missed timer.
- **Demo accounts by set-based SQL, not a C# loop, and one hash per role** (D7): a startup backfill step reads the
  stored password hash of one representative user per role (`S000001`, `L00001`, the demo `admin`), verifies it
  against that role's fixed demo password, and only recomputes and rewrites the hash when verification fails or the
  row is missing — at most three PBKDF2 operations per start, not 20,041. The insert itself is one `INSERT ... SELECT
  ... WHERE NOT EXISTS` per role against the whole `students`/`lecturers` tables, so a fresh account for every
  student or lecturer that does not already have one is a single round trip regardless of how many are missing.
  Sharing one password hash across every demo account of a role is acceptable specifically because these are
  published, deliberately public demo credentials, never real ones, and the sharing is confined to rows flagged
  `is_demo = true`.
- **Demo mode heals itself, and turning it off actually turns it off** (D29): every start with demo mode on re-enables
  any demo account a visitor managed to lock, disable, or leave mid-MFA-setup, and deletes stray tokens — a public
  demo that degrades every time someone pokes at it defeats the point of being public. The first start after demo
  mode is switched off disables every `is_demo` account outright (`disabled_at`, a rotated security stamp so any
  live session dies too) rather than leaving 20,041 accounts with a published password sitting live on a customer's
  database.
- Every backfill step, including these, commits its own transaction and writes its own idempotency row
  (`01-domain-and-data.md` section 6), so a crash between steps resumes cleanly at the next start rather than
  re-running (or skipping) work — this is the property `BackfillRecoveryTests` (stage S11) exercises directly by
  simulating a crash mid-run and asserting the restart completes and stays idempotent.

## Consequences

- A demo visitor cannot permanently break the public demo by locking accounts, closing the enrolment window, or
  letting the hot module (`CS3099`) fill up with self-enrolments that carry no mark — the next deploy or idle
  restart repairs all of it (`01-domain-and-data.md` section 6 steps 8 and 11).
- The whole publication lifecycle (schedule, cancel, unpublish, return to draft, correct) is a set of ordinary rows
  and ordinary reads; there is no separate "did the scheduler actually fire" failure mode to test for, only "does the
  query filter return the right rows for a given clock reading", which is exactly what `PublishTests` and
  `GradeVisibilityTests` assert with a fake clock.
- k6's `results-day.js` and `dashboard-knee.js` can authenticate as hundreds of genuinely distinct students without
  the startup cost of hashing that many passwords for real, which is what makes those scenarios' measurements
  represent a real cohort rather than one warmed cache line.

There is no v0 "before" load number for this ADR — v0 had no authentication and no publication lifecycle at all, so
the comparison is qualitative (a scheduler v0 never had, versus a stored instant) rather than a load run.

**v1 evidence (stage S13):** the demo re-run procedure in `docs/admin-guide.md` ("Re-run results day on the demo")
is the functional evidence; `load/k6/login-storm.js` and every authenticated scenario's `setup()` are the load
evidence that many distinct demo accounts authenticate cheaply. Recorded in [`docs/load-results/2026-10-02-v1-hardened.md`](../load-results/2026-10-02-v1-hardened.md) (setup and least-privilege
rehearsal): on a clone of the 20,000-student v0 database the demo-mode backfills completed in 8.6 s on the
application role, `demo_accounts` touching 40,080 rows in 2.4 s, and every load run signed in its 200-500 students in
`setup()` without error.
