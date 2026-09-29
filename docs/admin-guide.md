# Administrator guide

For the person running RushDay for an institution (or exploring the public demo as `admin`). Every action here is
available from `/admin` in the web app; the underlying route is named for anyone who wants to script it or read the
API directly (`src/RushDay.Api/RushDay.Api.http`, `docs/spec/02-api.md`).

Sign-in for an administrator always requires a TOTP second factor (D27) — the one exception is the public demo's own
`admin` account, which is exempt only while demo mode is on, so a visitor can see the admin surface without setting
up an authenticator app first. A real deployment's administrator sets up MFA on first login and there is no
exemption.

## 1. Enrolment windows

`/admin/windows` — one row per (academic year, semester): when self-enrolment opens, when it closes, and the
withdrawal deadline. Students can only enrol themselves while `now` is between `opens_at` and `closes_at`; they can
only withdraw themselves while `now` is before the withdrawal deadline **and** no submitted or published mark exists
for that module. Outside a window the catalogue still shows the module with its state (`notYetOpen`, `open`,
`closed`) and the instants, so students can plan ahead.

Create, edit or delete a window from this page. Changing the **academic year** in Settings (`/admin/settings`)
recomputes every module's `enrolledCount` to only the new year's active enrolments — this is deliberate (a module's
capacity is scoped to the current year, `docs/spec/00-overview.md` section 4.2) and happens immediately, not on the
next server restart.

## 2. Results: publish, reschedule, cancel, unpublish, return to draft, correct

`/admin/results` shows submission progress per module for a chosen (year, semester); a module with no students
enrolled is hidden by default. The lifecycle, in order:

1. **Draft** — a module's lecturers enter marks. Invisible to students and to any other module's lecturers.
2. **Submitted** — the module leader declares the module complete. Locked for lecturers from this point; an
   administrator can **return it to draft** with a reason if something needs correcting before publication (allowed
   any time before the module's results actually go live, including while a publication is scheduled but has not
   happened yet).
3. **Published** — an administrator publishes a whole (year, semester) at a chosen instant (`publishAt`), optionally
   posting a pinned announcement in the same action. `publishAt` can be up to 90 days in the future (a genuine
   results-day moment) or in the past (an immediate publish). While `publishAt` is still in the future the
   publication is **scheduled**: it can be **rescheduled** (a new instant) or **cancelled** (every grade goes back to
   Submitted) with no trace to students, because nothing was ever visible. Once live, it can be **unpublished** with
   a reason (grades go back to Submitted; this does leave an audit trail, because students may already have seen
   the marks).
4. **Correction** — at any point after submission (draft excluded — a draft mark answers `module-not-submitted`), a
   single mark can be corrected with a reason. A corrected published mark is visible to the student immediately,
   labelled "Amended {date}" rather than silently changed.

Every one of these actions is audited with a before/after value and, where relevant, the reason text
(`docs/spec/03-security.md` section 7 has the full action catalogue). None of them touch the database directly —
there is no SQL path for fixing a mark, on purpose, so "who changed this and why" is always answerable from the
audit log.

### Re-run results day on the demo

The seeded 2025/26 results are already published on every fresh deploy, so to see the actual results-day moment —
the countdown, the publish, the student's dashboard updating live — walk through it yourself against the 2026/27
data the demo backfill sets up for exactly this purpose:

1. Sign in as `L00001` (the demo lecturer). Go to **My modules → CS3001 → Marks**. The demo backfill has already
   enrolled 100 year-1 students on CS3001 for the current year with no marks yet. Enter marks (or absences/deferrals)
   for as many as you like, then use **Submit module** — CS3001 needs a mark or a non-mark outcome recorded for every
   active enrolment before it will let you submit.
2. Sign in as `admin`. Go to **Results**, select Autumn 2026/27, and publish it at an instant a few minutes in the
   future.
3. Open the login page (signed out, or in a private window) and `S000001`'s dashboard side by side. Both show a
   countdown to the same instant; the login page's public status and the dashboard's own poll both refresh
   automatically the moment it passes, with no manual reload.
4. To reset the demo back to "not yet published" for the next person: as `admin`, **unpublish** the Autumn 2026/27
   publication (if the instant has already passed) or **cancel** it (if it is still scheduled), then **return CS3001
   to draft**. The module is now exactly where step 1 started.

## 3. Overrides and trim

An administrator can act on a student's enrolments directly, bypassing the window and the credit limit, with a
mandatory reason recorded on every action:

- **Override-enrol**: ignores the window and the credit limit; module capacity still applies unless `forceCapacity`
  is set, which raises the module's capacity by exactly one place **and only when the module is already full** (it
  is not a general "ignore capacity" switch). Once a module's marks for the current year have been submitted,
  scheduled or published, nobody can join it, not even by override: the request is refused with `module-locked`.
  Return the module to draft first (section 2), enrol the student, and ask the leader to enter their mark and
  resubmit. This rule exists so that no student's mark can be left stranded outside a publication.
- **Override-withdraw**: withdraws a student from a module regardless of the withdrawal deadline, with a reason.
- **Trim to capacity** (`/admin/modules/{code}`): for a module that has drifted over capacity (the live database
  inherited an oversold `CS3099` from v0, and the ops page flags any module where `enrolledCount > capacity` as a
  data-quality warning), withdraws the most recent self-enrolments down to exactly the module's capacity, in one
  audited action, with a reason. This is the only supported way to fix an over-capacity module — never edit
  `enrolled_count` by hand.

## 4. Accounts and the second factor

`/admin/accounts` provisions a login for an existing student or lecturer record, or for a new administrator, showing
the temporary password exactly once (it is never retrievable again — provision a new one if it is lost). From here
you can lock, unlock, disable, enable, reset the password (always sets "must change password" on next login), and
**reset the second factor** — useful when an administrator loses their authenticator device; resetting clears their
TOTP enrolment and they are walked through setup again on next login, gated from every other route until they do
(`docs/spec/00-overview.md` section 8, "Identity and access").

Demo accounts (`is_demo = true`) are shown read-only here: they cannot be locked, disabled, or have their second
factor changed by hand, because the demo backfill heals them on every restart regardless (section 5 below) — any
change made through this page would simply be undone at the next deploy or idle restart.

## 5. The demo switch

`Demo:Enabled` is one setting, off by default, and it is the only thing that distinguishes the public demo from a
customer deployment running the exact same code:

- **On**: 20,000 students and 40 lecturers get logins (one shared password hash per role — real passwords are never
  computed this way), a demo `admin` account is exempt from the MFA requirement, the login page shows the three
  demo credentials, and a handful of one-time and every-start backfills keep the demo in a good state: the hot
  module (`CS3099`) has its self-enrolments-without-a-mark withdrawn on every start so it always has close to its
  30 places free, and any demo account a visitor managed to lock or disable is healed back to working order.
- **Off**: none of the above runs. Instead, the very next start **disables every account flagged `is_demo`**
  outright — not just stops issuing new demo logins, actively disables the existing ones and rotates their security
  stamp so any session already open for one of them dies too. `GET /api/public/status` then reports `demo: null`
  and the login page shows no credentials at all. This is checked by an integration test
  (`MigrationOnSeededDatabaseTests.Demo_off_disables_every_demo_account`) precisely because "turning demo mode off"
  needs to actually mean "no synthetic account can sign in any more", not just "no longer advertised".

A customer deployment should never turn this on. `render.yaml` sets neither `Demo__Enabled` nor
`Demo__PublicDemoAcknowledged` — the public demo sets both, by hand, only in the Render dashboard for this one
service.

**Demo reset** (`/admin/ops`, demo mode only): manually re-runs the same self-enrolment cleanup that already happens
on every restart, without waiting for a redeploy — useful right after a visitor has filled `CS3099` up during a demo
session and you want its places back immediately.

## 6. The `/story` page

`/story` is a public page, present and reachable with no login on **every** deployment, including a customer's own —
it is not a demo-only feature. It renders RushDay's own load-test evidence (the v0 baseline and, once stage S13
records it, the v1 comparison): the numbers this project's own README and ADRs cite, as charts, not a customer's own
operational data. It exists so a prospective buyer evaluating the project can see the measured story before they
have an account, and it never renders anything that would identify a real student — the underlying data is
`load/results/*.json`, committed to this repository, not read from any deployment's live database.

## 7. Single sign-on (extension point, not built)

v1 authenticates with local accounts (ASP.NET Core Identity) and TOTP for administrators. The next request any UK
university evaluating this project is expected to make is Microsoft Entra ID (or another SAML/OIDC) single sign-on.
Identity's external-login provider mechanism (`AddAuthentication().AddOpenIdConnect(...)` or
`AddMicrosoftIdentityWebApp`, mapped onto the existing `ApplicationUser` and role claims) is the intended extension
point — it slots in alongside the existing cookie session and role model without changing either, because a
federated sign-in still ends in the same `SignInManager` cookie issuance this application already uses. This is
recorded as a documented Should, not built in v1: it needs a real institutional tenant to configure and test against,
which this project does not have access to.

## 8. Retention

Audit rows (`audit_events`) are kept indefinitely in v1 — there is no purge job, scheduled or manual. A retention
purge (deleting or archiving audit rows older than a configured age) is a documented Could for a future release: if
built, it must itself run as an audited administrator action, and must drop and recreate the append-only trigger
inside its own transaction (the trigger otherwise blocks the purge's own `DELETE`s, which is the whole point of the
trigger — a purge is the one legitimate exception, and it must be visible in the log that it happened, by whom, and
what age threshold was used).

Personal data more broadly: see `docs/deployment.md` section 10 for data residency and the audited export routes
(`GET /api/me/export.json` for a student's own data; `GET /api/admin/students/{n}/export.json` for a subject-access
request handled by the academic office; `GET /api/admin/audit/export.csv` to answer "who has viewed or exported this
student's data").
