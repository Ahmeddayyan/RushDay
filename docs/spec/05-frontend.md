# RushDay v1 specification: 05. Front end

Scope: stack, configuration, structure, hosting contract, auth flow, data layer, routing, design system, page-by-page
specification, charts, accessibility, tests and build integration for the React SPA in `src/RushDay.Web`.
Decisions D4–D5, D19–D21, D26 of `00-overview.md` apply. API shapes are those of `02-api.md`; the SPA's TypeScript
types mirror them exactly.

## 1. Stack and versions

Runtime: Node 24.19.0, npm 11.17.0; `package.json` keeps `"engines": { "node": ">=22.12.0" }`, `"type": "module"`,
`"private": true`. The installed, lock-file-resolved set is kept (D20). Exact versions as installed:

| Dependency | Version | Role |
|---|---|---|
| `react`, `react-dom` | 19.3.0 | UI |
| `react-router` | 7.18.4 | data router: `createBrowserRouter`, `RouterProvider`, `Outlet`, `useSearchParams`, `redirect` |
| `@tanstack/react-query` | 5.104.0 | server state, caching, mutations |
| `react-hook-form` 7.89.0, `@hookform/resolvers` 5.9.1, `zod` 4.6.5 | | forms and client validation |
| `recharts` | 3.10.1 | charts on `/story` and `/admin/ops` only (own chunk) |
| `lucide-react` | 1.48.0 | icons, named imports only |
| **add** `@radix-ui/react-dialog` 1.1.23, `@radix-ui/react-alert-dialog` 1.1.23, `@radix-ui/react-dropdown-menu` 2.1.24, `@radix-ui/react-tooltip` 1.2.16, `@radix-ui/react-tabs` 1.1.21 | | accessible primitives |
| **add** `sonner` 2.0.8 | | toasts (live region) |
| **add** `@fontsource-variable/inter` 5.3.0 | | self-hosted UI font (CSP `font-src 'self'`) |

| Dev dependency | Version | Role |
|---|---|---|
| `typescript` | 6.0.3 | strict; `typescript-eslint` 8.70.1 supports `<6.1.0` |
| `vite` 8.3.1, `@vitejs/plugin-react` 6.1.1, `@tailwindcss/vite` 4.3.3, `tailwindcss` 4.3.3 | | build (Rolldown) and styling |
| `vitest` 5.0.2, `jsdom` 30.1.1, `@testing-library/react` 16.3.3, `@testing-library/jest-dom` 7.0.1, `@testing-library/user-event` 14.6.7 | | component tests |
| **add** `@vitest/coverage-v8` 5.0.2, `@testing-library/dom` 10.4.2, `msw` 2.15.0 | | coverage, DOM peer, HTTP mocking |
| `@playwright/test` 1.63.0, **add** `@axe-core/playwright` 4.13.0 | | end-to-end and accessibility scans |
| `eslint` 10.11.0, `@eslint/js` 10.0.1, `typescript-eslint` 8.70.1, `eslint-plugin-react-hooks` 7.1.1, `eslint-plugin-react-refresh` 0.5.7, `eslint-config-prettier` 10.1.8, `globals` 17.12.0, `prettier` 3.9.9 | | lint and format |
| `@types/react` 19.3.0, `@types/react-dom` 19.3.0, `@types/node` 24.19.0 | | types |

Not used, deliberately: `eslint-plugin-jsx-a11y` (no ESLint 10 support; axe in Playwright covers it),
`@tanstack/react-table` (tables are server-paginated or small), `date-fns` (Intl covers formatting), `axios`, `clsx` and
`tailwind-merge` (the hand-written `lib/cn.ts` stays), any global state library (session lives in one context).

`package.json` scripts:

```json
{
  "dev": "vite",
  "build": "tsc -b --noEmit && vite build",
  "preview": "vite preview",
  "typecheck": "tsc -b --noEmit",
  "lint": "eslint . --max-warnings 0",
  "format": "prettier --write .",
  "format:check": "prettier --check .",
  "test": "vitest run",
  "test:watch": "vitest",
  "test:coverage": "vitest run --coverage",
  "test:e2e": "playwright test",
  "test:e2e:ui": "playwright test --ui",
  "load-results": "node ../../load/summarize.mjs",
  "bundle-budget": "node scripts/bundle-budget.mjs"
}
```

## 2. Configuration files (exact)

### `vite.config.ts`

```ts
import { fileURLToPath, URL } from 'node:url'
import tailwindcss from '@tailwindcss/vite'
import react from '@vitejs/plugin-react'
import { defineConfig } from 'vite'

const apiTarget = process.env.VITE_API_PROXY ?? 'http://localhost:5080'

export default defineConfig({
  base: '/',
  plugins: [react(), tailwindcss()],
  resolve: { alias: { '@': fileURLToPath(new URL('./src', import.meta.url)) } },
  server: {
    port: 5173,
    strictPort: true,
    proxy: { '/api': { target: apiTarget, changeOrigin: false } },
  },
  build: {
    outDir: '../RushDay.Api/wwwroot',
    emptyOutDir: true,
    sourcemap: false,
    target: 'es2022',
    rolldownOptions: {
      output: {
        advancedChunks: {
          groups: [
            { name: 'react', test: /node_modules[\\/](react|react-dom|react-router|scheduler)[\\/]/ },
            { name: 'query', test: /node_modules[\\/]@tanstack[\\/]/ },
            { name: 'charts', test: /node_modules[\\/](recharts|d3-|victory-vendor|internmap|delaunator|robust-predicates)/ },
          ],
        },
      },
    },
  },
})
```

`changeOrigin: false` keeps `Host: localhost:5173`, so the API's `Set-Cookie` lands on `localhost` and the browser
returns it through the proxy; the API issues the cookie with `SecurePolicy=SameAsRequest` in Development. Only `/api`
is proxied: health and OpenAPI live under it.

### `vitest.config.ts`

Kept separate from the Vite config (unit tests need neither Tailwind nor the `wwwroot` output):

```ts
export default defineConfig({
  plugins: [react()],
  resolve: { alias: { '@': fileURLToPath(new URL('./src', import.meta.url)) } },
  test: {
    environment: 'jsdom',
    setupFiles: ['./src/test/setup.ts'],
    include: ['src/**/*.test.{ts,tsx}'],
    exclude: ['e2e/**', 'node_modules/**'],
    restoreMocks: true,
    unstubGlobals: true,
    css: false,
    coverage: { provider: 'v8', reporter: ['text', 'lcov'], include: ['src/**'],
                exclude: ['src/test/**', 'src/**/*.test.*', 'src/main.tsx'],
                thresholds: { 'src/api/**': { lines: 80 }, 'src/app/**': { lines: 80 }, 'src/lib/**': { lines: 80 } } },
  },
})
```

### TypeScript

`tsconfig.json` references `tsconfig.app.json` (browser: `"lib": ["ES2023", "DOM", "DOM.Iterable"]`, `"jsx": "react-jsx"`,
`"moduleResolution": "bundler"`, `"strict": true`, `"noUncheckedIndexedAccess": true`, `"exactOptionalPropertyTypes": true`,
`"verbatimModuleSyntax": true`, `"erasableSyntaxOnly": true`, `"noUnusedLocals"`, `"noUnusedParameters"`,
`"noFallthroughCasesInSwitch"`, `"noUncheckedSideEffectImports"`, `"paths": { "@/*": ["./src/*"] }`),
`tsconfig.node.json` (config files) and `tsconfig.e2e.json` (`e2e/**`, types `node`).

### `eslint.config.js` (flat, ESLint 10)

`@eslint/js` recommended, `typescript-eslint` `recommendedTypeChecked` with `parserOptions.projectService`,
`react-hooks` `flat.recommended`, `react-refresh` `vite`, `eslint-config-prettier` last. Custom rules:
`no-restricted-syntax` forbidding `JSXAttribute[name.name="dangerouslySetInnerHTML"]` (message: "render text; never
HTML") and `no-restricted-properties` on `document.cookie`. Globals: browser for `src/**`, node for configs and `e2e/**`.

### `index.html` (exact head; body has `<div id="root">` and a `<noscript>`)

```html
<!doctype html>
<html lang="en-GB">
  <head>
    <meta charset="utf-8" />
    <meta name="viewport" content="width=device-width, initial-scale=1" />
    <meta name="color-scheme" content="light dark" />
    <meta name="theme-color" media="(prefers-color-scheme: light)" content="#f4f5f7" />
    <meta name="theme-color" media="(prefers-color-scheme: dark)" content="#0f1216" />
    <title>RushDay</title>
    <meta name="description" content="I built RushDay because my own university's portal fell over every results day and enrolment window. I wanted to understand why that happens and to build a portal that doesn't crash under the same load." />
    <link rel="icon" href="/favicon.svg" type="image/svg+xml" />
    <script src="/theme-init.js"></script>
  </head>
  <body>
    <div id="root"></div>
    <noscript>RushDay needs JavaScript. Please enable it and reload.</noscript>
    <script type="module" src="/src/main.tsx"></script>
  </body>
</html>
```

`<title>RushDay</title>` is asserted by the integration test `IndexEndpointTests.Root_serves_the_spa_shell`. The
inline theme script of the current scaffold is replaced by `public/theme-init.js` (CSP `script-src 'self'`):

```js
(function () { try { var t = localStorage.getItem('rushday.theme'); if (t === 'light' || t === 'dark') document.documentElement.setAttribute('data-theme', t); } catch (e) {} })();
```

### Prettier

Existing `.prettierrc` stays (`semi: false`, single quotes, trailing commas, width 100).

## 3. Folder structure

```
src/RushDay.Web/
  package.json  package-lock.json  vite.config.ts  vitest.config.ts  playwright.config.ts
  tsconfig.json  tsconfig.app.json  tsconfig.node.json  tsconfig.e2e.json  eslint.config.js  .prettierrc  .prettierignore
  index.html
  scripts/bundle-budget.mjs                    fails when entry+react+query gzip > 220 KB or charts is in the entry graph
  public/  favicon.svg  theme-init.js  robots.txt  data/load-results.json   (generated by load/summarize.mjs, committed)
  e2e/     playwright fixtures and specs (section 13)
  src/
    main.tsx                                    createRoot(<StrictMode><Providers><RouterProvider/></Providers>)
    app/  providers.tsx  router.tsx  guards.tsx  AuthProvider.tsx  ErrorBoundary.tsx
    api/  client.ts  problem.ts  keys.ts  queryClient.ts
          endpoints/  auth.ts  public.ts  student.ts  modules.ts  announcements.ts  lecturer.ts  admin.ts  ops.ts
          types/      auth.ts  public.ts  common.ts  student.ts  modules.ts  lecturer.ts  admin.ts  ops.ts  loadResults.ts
    components/
      ui/      Button Input PasswordInput Textarea Select Checkbox FormField FormError Card Badge StatTile Meter
               Table Pagination SearchInput Dialog AlertDialog DropdownMenu Tooltip Tabs Skeleton EmptyState
               ErrorState ForbiddenState Spinner VisuallyHidden PageHeader ColdStartNotice
      layout/  AppShell Sidebar TopBar MobileDrawer BottomTabs UserMenu ThemeToggle SkipLink Footer
    features/
      auth/     LoginPage DemoAccounts ChangePasswordPage AccountPage schemas.ts routes.tsx
      shared/   AnnouncementsPage NotFoundPage ForbiddenPage AccessibilityPage routes.tsx
      student/  StudentDashboardPage ResultsPage TimetablePage CataloguePage ModuleDetailPage routes.tsx
                components/ ResultsSummaryCard NextClassesCard EnrolmentWindowCard CreditBudget ModuleCard CapacityMeter
                            EnrolButton MarkBand TimetableGrid TimetableAgenda ResultsTable
                hooks/ useDashboard useMyEnrolments useCatalogue useModule useEnrol useWithdraw   ics.ts
      lecturer/ LecturerHomePage MyModulesPage ModulePage (tabs) RosterTab MarksTab ModuleAnnouncementsTab routes.tsx
                components/ RosterTable MarksGrid MarksStatusChip SubmitDialog GradingProgress
                hooks/ useMyModules useRoster useMarks useSaveMarks useSubmitMarks useModuleAnnouncements
      admin/    AdminOverviewPage StudentsPage StudentSupportPage ModulesAdminPage ModuleAdminPage LecturersPage
                AccountsPage EnrolmentWindowsPage ResultsAdminPage AnnouncementsAdminPage AuditLogPage SettingsPage routes.tsx
                components/ CreateStudentDialog CreateLecturerDialog ProvisionAccountDialog TemporaryPasswordDialog
                            AccountRowActions ModuleEditDialog LecturerAssignmentEditor WindowEditor PublishDialog
                            RescheduleDialog ReturnToDraftDialog OverrideEnrolDialog OverrideWithdrawDialog
                            AnnouncementDialog AuditFilters AuditRow
                hooks/ one per endpoint group
      ops/      OpsPage routes.tsx  components/ LiveTiles PoolMeter RequestRateChart LatencyChart CountersPanel BackfillsTable DataQualityPanel
                charts/ EnrolmentRushChart DashboardKneeCharts ResultsDayTiles LoginStormTiles ChartCard ChartTable useLoadResults.ts
      story/    StoryPage routes.tsx
    lib/  cn.ts  format.ts  theme.ts  moduleCode.ts  returnTo.ts  useDocumentTitle.ts  useDebouncedValue.ts  download.ts  broadcast.ts
    styles/app.css
    test/  setup.ts  render.tsx  server.ts  factories.ts  handlers/ auth.ts public.ts student.ts modules.ts lecturer.ts admin.ts ops.ts
```

Migration of the current scaffold (stage S3): `src/lib/api.ts` → `src/api/client.ts` (extended per section 5),
`src/lib/queryClient.ts` → `src/api/queryClient.ts`, `src/lib/usePageTitle.ts` → `src/lib/useDocumentTitle.ts`,
`src/index.css` → `src/styles/app.css`, `src/lib/cn.ts` unchanged. Each feature area exports `routes.tsx`
(`RouteObject[]`); `app/router.tsx` imports all of them lazily and is written once, so stages S7–S10 never edit a
shared file (`06-implementation-plan.md`).

## 4. Hosting from the API (contract owned by the backend, `src/RushDay.Api/Hosting/SpaHosting.cs`)

`app.UseRushDaySpa()` runs after `SecurityHeadersMiddleware` and before endpoint mapping;
`app.MapRushDaySpaFallbacks()` runs after every `Map*`:

1. `UseDefaultFiles()` + `UseStaticFiles` with `OnPrepareResponse`: `/assets/*` → `Cache-Control: public, max-age=31536000, immutable`;
   everything else (`/index.html`, `/theme-init.js`, `/favicon.svg`, `/robots.txt`, `/data/*.json`) → `Cache-Control: no-cache`.
2. `app.MapFallback("/api/{**rest}", ...)` → 404 `urn:rushday:not-found` ProblemDetails, so an unknown API path is JSON,
   never `index.html`.
3. `app.MapFallbackToFile("index.html")` for every other unmatched GET (deep links such as `/student/results`).
4. When `wwwroot/index.html` is missing (local `dotnet run` without a web build), `GET /` and every non-`/api` path return
   `503 text/plain`: `Front end not built: run "npm run build" in src/RushDay.Web or use "npm run dev".`

Constraints the SPA places on the backend, all specified in `02-api.md` and `03-security.md`: CSP exactly as given
(`style-src 'unsafe-inline'`, strict scripts); 401/403 as JSON ProblemDetails, never redirects; `Me.csrfToken` in
`/auth/me` and `/auth/login`; `GET /api/auth/csrf` for the anonymous form; `SameSite=Strict` cookies.

## 5. Authentication flow in the SPA

### 5.1 Session state machine (`app/AuthProvider.tsx`)

```
status: 'booting' | 'anonymous' | 'authenticated'
user:   Me | null
csrf:   string | null       // request token, memory only, never persisted
```

1. On mount: `GET /api/auth/me`. 200 → `authenticated`, store `user` and `user.csrfToken`. 401 → `anonymous`, then
   `GET /api/auth/csrf` → store `csrf`. Network error or 5xx → stay `booting`, retry with backoff 1, 2, 4, 8 s up to 75 s
   total; after 3 s the splash shows `ColdStartNotice` ("Waking the server. On the free tier this can take up to a
   minute."); after 75 s an `ErrorState` with Retry.
2. `login(username, password)` → `POST /api/auth/login` with `X-CSRF-TOKEN: csrf`. 200 → `authenticated`, store the new
   `csrfToken` (the anonymous token is discarded). Errors surface in the form.
3. `logout()` → `POST /api/auth/logout` → `queryClient.clear()`, `anonymous`, fetch a fresh anonymous token, navigate to
   `/login`; `BroadcastChannel('rushday.auth')` message `{ type: 'logout' }` makes other tabs do the same.
4. Any `apiFetch` receiving 401 (except `/auth/me` and `/auth/login`) dispatches `rushday:unauthenticated`; the provider
   goes `anonymous`, clears the cache, shows one toast "Your session has ended. Sign in again." and the guard redirects
   with `returnTo`.
5. A 400 `urn:rushday:antiforgery` triggers one `GET /api/auth/me` (authenticated) or `GET /api/auth/csrf` (anonymous)
   and one retry of the original request; a second failure surfaces as an error.
6. `user.mustChangePassword` → every guarded route redirects to `/account/password?required=1` until
   `POST /api/auth/change-password` succeeds; then `/auth/me` is refetched and the original destination restored.

### 5.2 Guards (`app/guards.tsx`)

- `RootRedirect` (`/`): `booting` → splash; `anonymous` → `/login`; else → role home (`/student`, `/lecturer`, `/admin`).
- `PublicOnly` (`/login`): authenticated users go to a safe `returnTo`, else role home.
- `RequireAuth`: `booting` → splash; `anonymous` → `<Navigate to={'/login?returnTo=' + encodeURIComponent(pathname + search)} replace />`;
  `mustChangePassword` → `/account/password?required=1`.
- `RequireRole roles`: wrong role → `<Navigate to="/forbidden" replace />` (a real page Playwright can assert).
- `returnTo` is accepted only when it matches `/^\/(?!\/)[^\s]*$/` and does not start with `/login` (`lib/returnTo.ts`).

### 5.3 Fetch rules (`api/client.ts`)

```ts
apiFetch<T>(path: string, init?: { method?: 'GET'|'POST'|'PUT'|'DELETE'; body?: unknown; signal?: AbortSignal; expect?: 'json'|'blob'|'void' }): Promise<T>
```

- `credentials: 'same-origin'`, `Accept: application/json`; JSON bodies get `Content-Type: application/json`.
- Non-GET requests carry `X-CSRF-TOKEN`. Errors throw `ApiError { status, problem, kind, retryAfterSeconds }` where
  `kind` is the slug after the last colon of `problem.type` (`module-full`); a failed `fetch` throws `NetworkError`
  (status 0). 204 resolves `undefined`; `expect: 'blob'` returns a `Blob` for CSV and ICS downloads.
- Mutations are never retried; queries retry only on network errors and 5xx (section 6).

## 6. Data layer

### 6.1 QueryClient defaults (`api/queryClient.ts`)

```ts
queries: { staleTime: 30_000, gcTime: 5 * 60_000, refetchOnWindowFocus: false /* 20,000 tabs refocusing at 09:00 is a stampede */,
           retry: (n, err) => !(err instanceof ApiError && err.status > 0 && err.status < 500) && n < 2,
           retryDelay: (n) => Math.min(1000 * 2 ** n, 8000) },
mutations: { retry: 0 }
```

### 6.2 Query keys (`api/keys.ts`, written once in stage S5)

```
['index']                                    GET /api                              staleTime Infinity
['public','status']                          GET /api/public/status                staleTime 60s
['auth','me']                                GET /api/auth/me
['student','dashboard']                      GET /api/me/dashboard                 staleTime 60s
['student','results']                        GET /api/me/results
['student','timetable']                      GET /api/me/timetable
['student','enrolments']                     GET /api/me/enrolments                staleTime 15s
['modules','catalogue']                      GET /api/modules                      staleTime 15s
['modules', code]                            GET /api/modules/{code}               staleTime 5s
['announcements']                            GET /api/announcements                staleTime 60s
['lecturer','modules']                       GET /api/lecturer/modules
['lecturer','module', code, 'roster', { q, page, pageSize }]   placeholderData: keepPreviousData
['lecturer','module', code, 'marks']
['lecturer','module', code, 'announcements']
['admin','overview']                         refetchInterval 30s while mounted
['admin','settings']  ['admin','windows']  ['admin','results', semester]  ['admin','lecturers', q]
['admin','students', params]  ['admin','accounts', params]  ['admin','audit', params]    placeholderData: keepPreviousData
['admin','student', studentNumber]  ['admin','modules', includeInactive]  ['admin','announcements']
['admin','ops','metrics']                    refetchInterval 5s, refetchIntervalInBackground false
['load-results']                             GET /data/load-results.json           staleTime Infinity
```

### 6.3 Mutations

`useEnrol(code)` (`POST /api/me/enrolments`): `onMutate` cancels `['student','enrolments']` and `['modules', code]`,
snapshots both, appends `{ moduleCode: code, status: 'active', enrolledAt: now, canWithdraw: true }` to the enrolments
and decrements `placesRemaining` on the module; the `EnrolButton` shows "Enrolled" with a pending spinner and
`aria-busy`. `onError` restores snapshots and toasts `describeProblem(err)` (for `module-full`: "CS3099 filled up while
you were enrolling"; the card flips to Full). `onSettled` invalidates `['student','enrolments']`, `['student','dashboard']`,
`['modules', code]`, `['modules','catalogue']`. `useWithdraw(code)` mirrors it behind an `AlertDialog`
("Withdraw from CS3099? Your place may go to someone else.").

Lecturer mark saves are not optimistic (the server may reject rows with `stale-mark` or `not-enrolled-students`); the
grid shows saving state and applies the returned `MarksSheet`. Admin mutations invalidate their group key and
`['admin','overview']`.

### 6.4 ProblemDetails mapping (`api/problem.ts`)

`describeProblem(err): { title, message, action?: 'retry' | 'login' | 'wait' }` resolves by slug first, then status, then a
generic message with `traceId`. Copy per slug:

| Slug | Status | Copy |
|---|---|---|
| `validation` | 400 | field errors mapped to form fields via `setError`; unmapped keys to `FormError` |
| `antiforgery` | 400 | silent refresh and one retry |
| `invalid-credentials` | 401 | "Incorrect username or password." |
| `rate-limited` | 429 | "Too many attempts. Try again in {retryAfterSeconds}s." |
| `forbidden`, `not-your-module` | 403 | `ForbiddenState` |
| `password-change-required` | 403 | redirect to `/account/password?required=1` |
| `not-found`, `module-not-found`, `student-not-found`, ... | 404 | `EmptyState` "That page or record doesn't exist." |
| `already-enrolled` | 409 | "You're already enrolled on {code}." |
| `module-full` | 409 | "{code} is full." |
| `enrolment-window-closed` | 409 | "Enrolment for {semester} is closed. It opens {opensAt, relative}." (from extensions) |
| `withdrawal-deadline-passed` | 409 | "The withdrawal deadline for {code} was {withdrawalDeadlineAt}." |
| `results-exist` | 409 | "You can't withdraw from a module with submitted or published marks. Contact the academic office." |
| `not-enrolled` | 404 | "You're not enrolled on {code}." |
| `credit-limit-exceeded` | 422 | "That would take you over {limit} credits for {semester}." |
| `module-locked` | 409 | "Marks for this module are submitted and can't be changed." |
| `stale-mark` | 409 | "Someone else changed {n} marks. Review the highlighted rows and save again." |
| `not-enrolled-students` | 422 | "{n} students are no longer enrolled: {list}." |
| `marks-incomplete` | 422 | "{n} students have no mark yet." (dialog lists `missing`) |
| `capacity-below-enrolled` | 422 | "Capacity can't go below the {enrolledCount} students already enrolled." |
| `self-lockout` | 422 | "You can't lock or disable your own account." |
| `weak-password` | 400 | list from `errors.newPassword` |
| `invalid-current-password` | 400 | "Your current password is incorrect." |
| `server-busy`, `timeout` | 503 | "The portal is very busy right now. Try again in a moment." + Retry |
| anything else 5xx | | "Something went wrong on our side. Reference {traceId}." |

## 7. Routing and navigation

React Router 7 `createBrowserRouter`; each area is a lazy route module so a student never downloads admin code.
Every route sets its title through `useDocumentTitle('Results · RushDay')` and moves focus to the page `<h1>` on
navigation.

| Path | Guard | Page |
|---|---|---|
| `/` | none | `RootRedirect` |
| `/login` | `PublicOnly` | `LoginPage` (`?returnTo=` honoured) |
| `/story` | none | `StoryPage` (public) |
| `/accessibility` | none | `AccessibilityPage` (public statement) |
| `/forbidden`, `*` | none | `ForbiddenPage`, `NotFoundPage` |
| `/account`, `/account/password` | `RequireAuth` | `AccountPage`, `ChangePasswordPage` (`?required=1` hides navigation, keeps Sign out) |
| `/announcements` | `RequireAuth` | `AnnouncementsPage` |
| `/student`, `/student/results`, `/student/timetable`, `/student/modules`, `/student/modules/:code` | `RequireRole Student` | student pages; catalogue filters in the URL (`?q=&semester=&level=&dept=&availability=&mine=`) |
| `/lecturer`, `/lecturer/modules`, `/lecturer/modules/:code` (roster), `/lecturer/modules/:code/marks`, `/lecturer/modules/:code/announcements` | `RequireRole Lecturer` | lecturer pages |
| `/admin`, `/admin/students`, `/admin/students/:studentNumber`, `/admin/modules`, `/admin/modules/:code`, `/admin/lecturers`, `/admin/accounts`, `/admin/enrolment`, `/admin/results`, `/admin/announcements`, `/admin/audit`, `/admin/ops`, `/admin/settings` | `RequireRole Admin` | admin pages; list filters in the URL |

Navigation (sidebar ≥ 1024 px, drawer below; students also get `BottomTabs` below 768 px):

- Student: Home, Results, Timetable, Modules, Announcements (bottom tabs: Home, Results, Timetable, Modules).
- Lecturer: Home, My modules, Announcements.
- Admin: Overview, Students, Modules, Lecturers, Accounts, Enrolment windows, Results, Announcements, Audit log,
  Operations, Settings.
- All: `UserMenu` (display name, role badge, Account, Sign out), `ThemeToggle`, `Footer` "Deployed {commit} · {institution}
  · Accessibility · GitHub" from `['index']` and `['public','status']`.

## 8. Static load-results data

`GET /data/load-results.json` (from `public/`, generated by `load/summarize.mjs` from `load/results/*.json` and
`load/results/runs.json`):

```ts
interface LoadResults { generatedAt: string; machine: string; runs: LoadRun[] }
interface LoadRun {
  id: string; scenario: 'enrolment-rush' | 'results-day' | 'dashboard-knee' | 'login-storm'; version: 'v0' | 'v1';
  label: string; ranAt: string; source: string; targetRate?: number; notes?: string;
  metrics: { requests: number; failedRate: number; p50Ms: number; p95Ms: number; p99Ms?: number; maxMs: number;
             achievedRate?: number; droppedIterations?: number;
             accepted?: number; rejectedFull?: number; rejectedOther?: number; shed?: number; errored?: number;
             capacity?: number; oversold?: number; loginsOk?: number; loginsRateLimited?: number }
}
```

The summariser reads `metrics.http_req_duration.{med,"p(95)","p(99)",max}` (p99 is absent from the v0 results-day
summary and stays optional), `http_req_failed.value`, `http_reqs.{count,rate}`, `dropped_iterations.count` and the
custom counters. Until v1 runs are committed the charts render the "after" series as "not yet measured".

## 9. Design system

### 9.1 Tokens (`src/styles/app.css`)

The scaffold's token names are kept and extended. Colours are defined once on `:root`, redefined for dark under
`@media (prefers-color-scheme: dark) { :root:not([data-theme="light"]) }` and again under `:root[data-theme="dark"]`, and
exposed to Tailwind through `@theme inline`. Components use `bg-surface`, `text-muted`, `border-border`; never `dark:`.

```css
@import 'tailwindcss';
@import '@fontsource-variable/inter';

:root {
  color-scheme: light;
  --background: #f4f5f7; --surface: #ffffff; --surface-2: #f8f9fb; --border: #e2e5e9; --border-strong: #c9ced6;
  --text: #1a1d21; --muted: #5f6672; --subtle: #7b8390;
  --primary: #2f5bea; --primary-hover: #244bcc; --primary-foreground: #ffffff; --primary-soft: #eef2ff;
  --success: #1f8a4c; --success-soft: #e8f5ec; --warning: #8a6d1f; --warning-soft: #fbf3dc;
  --danger: #b42323; --danger-soft: #fbe6e6; --info: #2f5bea; --info-soft: #eef2ff; --focus: #2f5bea;
  --chart-accent: #2f5bea; --chart-accent-2: #86b6ef; --chart-accent-3: #104281; --chart-muted: #898781;
  --chart-grid: #e1e0d9; --chart-critical: #d03b3b;
  --radius: 12px; --shadow-sm: 0 1px 2px rgb(0 0 0 / 0.06); --shadow: 0 4px 16px rgb(0 0 0 / 0.08);
}
/* dark values (both selectors): color-scheme: dark; --background:#0f1216; --surface:#171b21; --surface-2:#1e232b; --border:#2a3038;
   --border-strong:#3a424d; --text:#e8eaed; --muted:#9aa3ad; --subtle:#7f8894; --primary:#8aa4ff; --primary-hover:#a3b7ff;
   --primary-foreground:#0f1216; --primary-soft:#1e2534; --success:#5ccb85; --success-soft:#15301f; --warning:#e3c15a;
   --warning-soft:#33290f; --danger:#f07575; --danger-soft:#3a1717; --info:#8aa4ff; --info-soft:#1e2534; --focus:#8aa4ff;
   --chart-accent:#3987e5; --chart-accent-2:#6da7ec; --chart-accent-3:#184f95; --chart-muted:#898781; --chart-grid:#2c2c2a;
   --chart-critical:#d03b3b; --shadow-sm:none; --shadow:none; */
@theme inline { --color-background: var(--background); ... --color-focus: var(--focus);
  --font-sans: 'Inter Variable', system-ui, -apple-system, 'Segoe UI', Roboto, sans-serif;
  --font-mono: ui-monospace, 'Cascadia Mono', 'Segoe UI Mono', Menlo, Consolas, monospace;
  --radius-sm: calc(var(--radius) - 6px); --radius-md: calc(var(--radius) - 4px); --radius-lg: var(--radius); --radius-xl: calc(var(--radius) + 4px); }
@layer base { html { font-family: var(--font-sans); } body { background: var(--background); color: var(--text); line-height: 1.5; min-height: 100dvh; }
  :focus-visible { outline: 2px solid var(--focus); outline-offset: 2px; }
  @media (prefers-reduced-motion: reduce) { *, ::before, ::after { animation-duration: 0.01ms !important; transition-duration: 0.01ms !important; } } }
@media print { .no-print { display: none !important; } body { background: #fff; color: #000; } }
```

Contrast (checked): `--muted` on `--surface` 5.6:1 light / 6.8:1 dark; white on `#2f5bea` 5.5:1; `#0f1216` on
`#8aa4ff` 9.5:1; `--danger` on `--surface` 6.9:1 light / 6.5:1 dark. Status colours are always paired with an icon and a
text label. `ThemeProvider` (`lib/theme.ts`): `'system' | 'light' | 'dark'` in `localStorage['rushday.theme']` (try/catch),
applied as `data-theme` (removed for system) and mirrored to `<meta name="theme-color">`.

### 9.2 Typography, spacing, layout

Inter Variable, 16 px base, line height 1.5. `text-xs` 12 captions, `sm` 14 table cells and hints, `base` 16 body,
`lg` 18 card titles, `xl`/`2xl` page h1 (mobile/desktop), `4xl` 36 hero figures. Numbers in tables use `tabular-nums`;
module codes and usernames `font-mono`. 4 px grid; gutters 16 px (< 768), 24 px (≥ 768), 32 px (≥ 1280); content max
1200 px (admin tables and ops 1400 px); cards `bg-surface border border-border rounded-lg p-4 md:p-6 shadow-sm`;
sidebar 264 px.

### 9.3 Component patterns

- Buttons: `primary`, `secondary`, `ghost`, `danger`; heights 36/40/44; ≥ 44 px on coarse pointers; loading keeps
  width, shows `Spinner`, sets `aria-busy` and `disabled`; icon-only buttons require `aria-label` (typed prop).
- Forms: `FormField` renders `<label for>`, hint (`aria-describedby`), error (`role="alert"` on the first error only,
  `aria-invalid`), "required" as text. Validation on blur, then on change after the first submit. Server `errors` map
  by camelCase key.
- Tables: `<table>` with `<caption>` (visually hidden when a heading is adjacent), `<th scope="col">`, sortable headers as
  `<button aria-sort>`; below 640 px rows stack as cards with `data-label` headers; opt-in `scroll="x"` for audit and
  accounts tables on tablets with a fade affordance.
- Dialogs: Radix `Dialog` (focus trap, Escape, focus restore), max 480 px, full-screen sheet below 640 px; destructive
  confirmations always `AlertDialog` with the consequence in the description.
- Empty/Error/Forbidden states: icon (`aria-hidden`), one-sentence heading, one-sentence explanation, one action.
- Badges/chips: soft background + text + icon; never colour alone (Draft with a pencil, Submitted with a clock,
  Published with a check).
- Responsive to 360 px: single column below 768; timetable grid → agenda; chart cards stack; marks grid → card rows with a
  sticky bottom action bar; filter rows wrap; no horizontal page scroll (Playwright asserts
  `scrollWidth <= innerWidth` on the mobile project).

## 10. Page-by-page specification

Conventions for every page: `PageHeader` (h1, optional description, primary action slot); loading shows `Skeleton`
shapes matching the final layout; refetches keep the old content at 60% opacity; `ErrorState` with `describeProblem`
copy and Retry; 403 → `ForbiddenState`; empty states explain what fills the page and link to the action.

### `/login` (public)

- Centred card (max 420 px): institution name and RushDay wordmark from `['public','status']`; **strapline = the story
  sentence pair, verbatim**; link "How it holds up under load" → `/story`.
- Form (react-hook-form + zod `loginSchema`: username trimmed 1..64, password 1..128): username `Input`
  (`autocomplete="username"`, no case transform, server compares case-insensitively), `PasswordInput`
  (`autocomplete="current-password"`, show/hide toggle with `aria-pressed`), primary "Sign in" (`aria-busy` while
  pending), `FormError` `role="alert"` with 401 "Incorrect username or password.", 429 "Too many attempts. Try again in
  {n}s.", 503 busy copy.
- `DemoAccounts` panel only when `status.demo` is not null: three rows (Student `S000001`, Lecturer `L00001`,
  Administrator `admin`) with the password in `<code>`, the hint, and a "Use" button that fills the form and focuses
  Sign in; the note "Demo data: 20,000 synthetic students, no real people."
- Results-day line: "Autumn 2025/26 results publish 28 Sep 2026, 10:00 (Europe/London)" with a live countdown from
  `status.nextPublication` while in the future; hidden when null.
- Cold start: after 3 s pending, `ColdStartNotice`. After success: `returnTo` or role home; `mustChangePassword` →
  `/account/password?required=1`.
- Footer: deployed commit, privacy note ("Only your session cookie is stored. No third-party scripts."), links to
  `/accessibility`, GitHub, "Forgotten your password? Contact the academic office." No registration link.
- Booting splash while `/auth/me` is unresolved so a signed-in user never sees the form flash.

### `/story` (public)

The story sentence pair in the first person as the opening, then the three-act narrative (baseline, break it, fix it),
then chart cards from `features/ops/charts` (section 11) each with the exact numbers, the source file and the ADR link,
then "See it running" → `/login`. Data: `['load-results']`, `['index']`.

### `/accessibility` (public)

Statement page: conformance target (WCAG 2.2 AA), how it is tested, known limitations (charts have table twins; PDF
export is browser print), contact route (academic office), date of the statement.

### `/forbidden`, `*`

`ForbiddenPage`: "You don't have access to that page", who it is for, button to role home or Sign in.
`NotFoundPage`: same shape.

### `/account`, `/account/password`

Profile card (display name, username, role badge, student or staff number, programme and year for students from
`['student','dashboard']`); "Change password" link; Appearance `ThemeToggle` (System / Light / Dark, `role="radiogroup"`).
`ChangePasswordPage`: current, new, confirm (zod: min 12, must differ from current; server policy errors mapped);
live checklist under the new-password field (`aria-live="polite"`); `?required=1` banner "Your administrator set a
temporary password. Choose a new one to continue." with navigation hidden; success → toast, refetch `['auth','me']`,
navigate to stored `returnTo` or home.

### `/announcements` (any role)

Cards, pinned first (pin icon + "Pinned"), title, scope badge (University / module code), author, absolute date with
relative in tooltip, body in `white-space: pre-line`. Empty: "No announcements yet." Data: `['announcements']`.

### `/student` (dashboard)

- Header: time-of-day greeting with first name, student number, programme, year.
- Grid (2 columns ≥ 1024): `ResultsSummaryCard` (weighted average hero figure, classification badge, "n modules
  graded", link "All results"; when empty and `nextPublication` exists: "Results publish {date} at {time} ({zone})" with a
  countdown, else "No results published yet"); `EnrolmentWindowCard` (state per semester from `enrolmentWindows`,
  `CreditBudget` "45 of 60 credits, Spring", CTA "Browse modules"); `NextClassesCard` (today's remaining slots else the
  next day with classes; link "Full timetable"); `AnnouncementsCard` (latest 3, link to all); enrolled modules list (code,
  title, credits, semester, link to detail).
- Data: `['student','dashboard']` alone (one request).

### `/student/results`

`ResultsTable` grouped by semester (`<table>` per group with `<caption>`): code (mono), title, credits, mark (tabular),
`MarkBand` text ("First", "2:1", "2:2", "Third", "Fail"; icon only for Fail, which also uses the danger token), published
date. Scheduled semesters show "Publishes {instant}"; pending show "Marks not yet submitted". Summary row: weighted
average, classification, `<details>` "How this is calculated" matching `Classification.cs`. "Print or save as PDF" (print
stylesheet renders a clean transcript). Data: `['student','results']`.

### `/student/timetable`

≥ 768 px `TimetableGrid` (Monday–Friday, 09:00–18:00, 30-minute lines, positioned blocks with code, room, time, today's
column highlighted, "now" line in hours, overlaps split the column); < 768 px `TimetableAgenda` (day tabs, default today).
"Add to calendar (.ics)" builds weekly recurring VEVENTs client-side (`features/student/ics.ts`) and downloads via
`lib/download.ts`. Empty: "No classes yet. Enrol on modules to build your timetable." Data: `['student','timetable']`.

### `/student/modules` (catalogue)

Filter row (URL-synced): `SearchInput` (code or title, debounced 250 ms), semester (All/Autumn/Spring), level (All/1/2/3),
department (All/CS/MA/PH/EE, derived client-side from the code in `lib/moduleCode.ts` and also present on the
`ModuleSummary`), availability (All/Places available/Full), "Enrolled only". Result count with `aria-live="polite"`.
`CreditBudget` bars for both semesters. Grid of `ModuleCard`s (2 columns ≥ 768, 3 ≥ 1280): code + title, credits and
semester, leader, `CapacityMeter` (fill = enrolledCount/capacity, text "12 of 30 places left" always present),
`EnrolButton` states:

- not enrolled, window open, places → primary "Enrol";
- not enrolled, full → disabled "Full" with the caption;
- not enrolled, window `closed`/`notYetOpen`/`noWindow` → disabled "Enrolment closed" with the opening instant when known;
- would exceed 60 credits → enabled with the caption "Over your credit limit" (the server's 422 is the authority);
- enrolled → secondary "Enrolled" plus a ghost "Withdraw" when `canWithdraw` (deadline shown in the confirm dialog).

Optimistic behaviour per section 6.3. CS3099 gets no special treatment in code. Data: `['modules','catalogue']`,
`['student','enrolments']`.

### `/student/modules/:code`

Header (code, title, badges for credits, semester, level, lecturers), large `CapacityMeter`, description, timetable
slots, "Your status" panel (enrolled since / your mark and band when published / not enrolled), the same CTA. 404 →
`EmptyState`. Data: `['modules', code]`, `['student','enrolments']`, `['student','results']`.

### `/lecturer`

Stat tiles (modules taught, students across them, marks entered, marks missing); `GradingProgress` rows per module with
a segmented meter (published / submitted / draft / missing as ordinal accent steps plus grey, legend and numbers) and CTA
"Enter marks"; announcements card. Data: `['lecturer','modules']`, `['announcements']`.

### `/lecturer/modules`

Table: code, title, semester, enrolled/capacity, marks status chip, entered/missing, my role, actions (Roster, Marks,
Announcements). Client-side sort. Empty: "No modules are assigned to you. Ask an administrator."

### `/lecturer/modules/:code` (tabs Roster | Marks | Announcements)

- **Roster**: server-paginated (50/page) `RosterTable` with search; columns number (mono), name, programme, year,
  enrolled on, status chip (Active / Withdrawn). "Export CSV" (Should). 403 → `ForbiddenState` "This module isn't
  assigned to you."
- **Marks** (`/marks`): top bar with status chip and counts (entered / missing), "Save marks" (disabled until dirty;
  "3 unsaved"), "Submit module" (primary; `SubmitDialog`: "Submit {n} marks for CS3099? Marks are locked for editing and
  go to the academic office for publication." lists missing students and disables submit when any are missing).
  `MarksGrid`: rows for active enrolments first, then withdrawn (greyed, no input); per row number, name, mark
  `<input type="number" inputmode="numeric" min=0 max=100 step=1>` (locked with a lock icon when status is not draft),
  updated at, entered by, per-row error. Keyboard: Enter or ArrowDown to the next input, ArrowUp previous; pasting a
  column of numbers fills consecutive rows; dirty rows get a left accent border; `beforeunload` while dirty. Save sends
  only dirty rows with their `version`; `stale-mark` rows stay dirty and highlighted with the server's value shown.
  After submit: banner "Submitted on {date}. Marks are read-only until the academic office publishes them."
- **Announcements**: list with future/expired badges; New / Edit (`AnnouncementDialog`: title ≤ 120, body ≤ 4,000 with
  live count, pinned, publish at, expires at) / Delete (`AlertDialog`).
- Data: `['lecturer','module', code, ...]`, `['lecturer','modules']` for the header.

### `/admin` (overview)

Stat tiles (students, lecturers, modules, active enrolments, accounts, locked), window chips per semester, "Results:
{submitted} of {total} modules submitted for {semester}" with CTA "Publish results", database status, quick actions
(Provision account, Create student, Publish results, New announcement), recent activity (10 `AuditRow`s). Data:
`['admin','overview']`.

### `/admin/students`, `/admin/students/:studentNumber`

Search (number or name), paged table (25/page): number, name, programme, year, account state chip, action "View".
"Create student" → `CreateStudentDialog`. `StudentSupportPage`: banner "Viewing as administrator", the student's
record, account card (state, last login, actions: Provision / Lock / Unlock / Disable / Enable / Reset password), enrolments
table with status and source, grades table **with status badges** and `visibleToStudent`, weighted average as the
student sees it, override actions (`OverrideEnrolDialog`: module code combobox, reason ≥ 10 chars, "Raise capacity by
one if full" checkbox; `OverrideWithdrawDialog`: reason), recent audit. Data: `['admin','students', params]`,
`['admin','student', n]`.

### `/admin/modules`, `/admin/modules/:code`

Table (client-side filter and sort, 121 rows): code, title, semester, credits, capacity, enrolled, leader, marks status,
active. "New module" and Edit → `ModuleEditDialog` (title, description, credits, capacity with warning "Below current
enrolment ({n})" that the server rejects, semester, active). `ModuleAdminPage`: details, `LecturerAssignmentEditor`
(rows staffNumber combobox from `['admin','lecturers', q]` + role; exactly one leader enforced client-side and by the
server), timetable slots (read-only), marks status. Data: `['admin','modules', includeInactive]`.

### `/admin/lecturers`

Table: staff number, name, title, department, modules, account state; "Create lecturer" → `CreateLecturerDialog`.

### `/admin/accounts`

Filters (search, role, state; URL-synced), paged table (25/page): username (mono), display name, role badge, linked
number, state chips (Locked / Disabled / Must change password / Demo), last sign-in, `AccountRowActions` (Reset
password, Lock/Unlock, Disable/Enable, View student). "Provision account" → `ProvisionAccountDialog` (role first; Student
→ student number combobox from `['admin','students']` with hasAccount=false; Lecturer → staff number combobox; Admin →
none; optional temporary password) → `TemporaryPasswordDialog` shows the password once with Copy and "This won't be shown
again." Demo banner when any `isDemo` account exists: "Demo accounts are present. Turn Demo__Enabled off before real use."

### `/admin/enrolment` (windows)

One `WindowEditor` card per window (academic year, semester, opens, closes, withdrawal deadline as `datetime-local` in
the institution zone with the zone named), state chip, Save, Delete; "Add window"; shortcuts "Open now for 7 days",
"Close now". Client validation mirrors `window-dates-invalid`.

### `/admin/results`

Semester picker (and academic year from settings). `SubmissionProgressTable`: code, title, leader, enrolled,
entered/missing, status chip, "Return to draft" action (`ReturnToDraftDialog` with reason). `PublishDialog`: date-time
picker (default now, max +90 days) in the institution zone, preview "Will publish {n} submitted modules ({grades}
marks); {m} modules excluded (draft)" and the sentence "This is the results-day button. Students see these marks at the
chosen instant." Publication history table with state chips and "Reschedule" for scheduled ones (`RescheduleDialog`).
Data: `['admin','results', semester]`.

### `/admin/announcements`

List with scope badge, pinned, published/expires, author; New (university scope) / Edit / Delete.

### `/admin/audit`

`AuditFilters` (actor, action select from the catalogue, student number, module code, date range presets Today / 7 / 30
days / custom; URL-synced). Table (50/page): time (absolute; relative in tooltip), actor + role, action label, subject,
student, module, expand control showing `details` as a definition list (text only). "Export CSV" downloads via the same
filters. Empty: "Nothing recorded for these filters."

### `/admin/ops`

Header: commit, environment, uptime, "sampled {n}s ago" (`aria-live="polite"`, one update per sample). `LiveTiles`
(60-sample client-side sparklines): requests/s, in flight, p95, p99, 5xx, 429, 503 shed, working set MB, GC heap MB.
`PoolMeter`: busy/max with pending requests and "wait timeouts since start: {n}" (the headline v0 failure).
`RequestRateChart` and `LatencyChart` (last 60 minutes from `series`). `CountersPanel`: enrolment outcomes, cache hit
ratio per cache, auth counters, dashboard queries per request. "The load story": the same chart cards as `/story` with
Chart/Table toggles. `BackfillsTable`, `DataQualityPanel` (modules over capacity, drift, "Reconcile now" button →
`POST /api/admin/ops/reconcile`). Note: "Alerting is out of scope on the free tier; this page is pull-only."
Data: `['admin','ops','metrics']` (5 s, paused when hidden), `['load-results']`.

## 11. Charts (`features/ops/charts`)

Method: choose the form, assign colour by job (emphasis scheme: accent for "after", grey for "before", status red only
for genuine errors), validate against RushDay's surfaces, then marks, hover and a table twin. Palette validation:
`#2f5bea` on `#ffffff` and `#3987e5` on `#171b21` pass; `#8aa4ff` fails the dark lightness band and is never a chart
mark; `#898781` is the de-emphasis grey; red/green status pairs fail deutan separation, so status is always icon +
label.

| Chart | Form | Encoding |
|---|---|---|
| `EnrolmentRushChart` | Hero figure "Oversold places: 124 → 0" above two horizontal 100%-stacked bars (v0, v1) of 500 requests: Accepted (accent), Rejected as full (grey, the correct outcome), Shed 429/503 (accent-2), Errored (critical). 2 px surface gaps; legend; values in tooltip and table. | part-to-whole, emphasis + status |
| `DashboardKneeCharts` | Three small multiples sharing the x-axis (target rate 1,000–4,000 rps): p95 latency (ms), achieved throughput (rps), dropped + shed. Two series each: v0 (grey, 2 px line, 8 px markers with surface ring) and v1 (accent). One y-axis per chart; end labels "v0"/"v1"; legend. | change across load |
| `ResultsDayTiles` | KPI row: peak rate 800/s, p95, p99, failed %, queries per dashboard (15 → 5), each showing v0 and v1 with the delta. | headline numbers |
| `LoginStormTiles` | KPI row: logins/s reached, 200 vs 429 counts, p95. | headline numbers |
| `RequestRateChart` (live) | Single-series line of requests/s over the last 60 minutes; area wash 10%; no legend. | trend |
| `LatencyChart` (live) | p50/p95/p99 as ordinal steps (`--chart-accent-2`, `--chart-accent`, `--chart-accent-3`), legend + end labels. | ordinal |
| `PoolMeter` | Meter with same-hue lighter track; warning at 80%, danger at 95% with a text label. | ratio |

Shared rules: bars ≤ 24 px with 4 px rounded data-ends; hairline gridlines in `--chart-grid`; axis and label text in
text tokens; tooltips list every series at the hovered x; containers include the axis band (no nested scroll);
`ChartCard` wraps each chart with title, one-sentence finding, source file, ADR link and `Tabs` Chart / Table where
`ChartTable` renders the identical numbers; `<figure aria-labelledby>` with a visually hidden summary sentence;
`isAnimationActive={false}` under `prefers-reduced-motion`. Recharts is only in the `charts` chunk, loaded by `/story` and
`/admin/ops`.

## 12. Accessibility (WCAG 2.2 AA)

- Structure: `SkipLink` to `#main`; landmarks `header`, `nav aria-label="Primary"`, `main`, `footer`; one `h1` per page;
  document title per route; focus to `h1` on route change; `lang="en-GB"`.
- Keyboard: every action reachable; visible focus ring (2 px, offset 2 px); dialogs trap and restore focus; Radix
  arrow-key patterns for menus and tabs; marks grid Enter/Arrow navigation; Escape closes overlays; no traps.
- Forms: programmatic labels, `autocomplete` tokens, errors announced and linked, required in text.
- Colour: never colour alone; contrast ≥ 4.5:1 text, ≥ 3:1 UI boundaries and focus rings in both themes; legends for
  every multi-series chart.
- Motion: `prefers-reduced-motion` disables sparkline animation and transitions.
- Live regions: sonner toasts (`role="status"`), result counts, live sample timestamp (throttled), optimistic enrol status.
- Targets ≥ 44 × 44 on coarse pointers; ≥ 24 × 24 elsewhere. Usable at 200% zoom and 320 px width.
- Dates: absolute always shown; zone named where ambiguous (windows, publication instants).
- Downloads announced in the label ("Export CSV, downloads a file").
- Verification: axe (`wcag2a, wcag2aa, wcag22aa`) in Playwright on every page in both themes; zero `serious`/`critical`.

## 13. Tests

### 13.1 Vitest + Testing Library + MSW (`src/**/*.test.tsx`, `src/test/`)

`renderWithProviders(ui, { route, user, handlers })` wraps a fresh `QueryClient` (retries off), `MemoryRouter` and an
`AuthProvider` primed with a `Me` fixture; MSW `server` in `setup.ts` with handlers per endpoint group and factories for
`DashboardResponse`, `ModuleSummary`, `MarksSheet`, `AccountView`, `AuditEventView`, `OpsSnapshot`.

Required tests: `api/client.test.ts` (CSRF header on non-GET only; `ApiError.kind`; 401 event; antiforgery refresh and
single retry; blob), `api/problem.test.ts` (every slug maps; unknown 5xx includes `traceId`), `app/guards.test.tsx`
(anonymous → `/login?returnTo=`; wrong role → `/forbidden`; must-change → password page; `returnTo` sanitiser rejects
`//evil` and `/login`), `app/AuthProvider.test.tsx` (boot 200/401; cold-start notice at 3 s with fake timers; logout
clears cache), `features/auth/LoginPage.test.tsx` (validation; demo "Use"; 401/429 copy; redirect), `ChangePasswordPage`
(checklist; server errors; required mode hides nav), `student/CataloguePage` (filters and URL sync; button states;
live count), `student/hooks/useEnrol` (optimistic add; rollback on `module-full` with the toast; invalidation),
`useWithdraw`, `student/ResultsPage` (grouping; bands for 70/60/50/40/39; weighted average 80×30 + 50×15 → 70.0;
scheduled copy), `student/TimetableGrid` and `ics.test.ts`, `lecturer/MarksGrid` (0–100; dirty count; Enter moves focus;
locked rows; `stale-mark` rows stay dirty; submit dialog lists missing), `admin/AccountsPage` (paging keeps data; provision
→ temporary password shown once), `admin/ResultsAdminPage` (publish preview copy; max +90 days; zone rendering),
`admin/AuditLogPage` (filters → query params; details as text: a `<script>` string renders as text), `ops/charts/*`
(table twin equals chart data; "not yet measured"), `components/ui/*` (Button loading; FormField wiring; Table stacked
mode; ThemeToggle with a throwing `localStorage`).

### 13.2 Playwright (`e2e/`, `playwright.config.ts`)

```ts
export default defineConfig({
  testDir: './e2e', fullyParallel: false, forbidOnly: !!process.env.CI, retries: process.env.CI ? 1 : 0,
  reporter: [['list'], ['html', { open: 'never' }]],
  use: { baseURL: process.env.E2E_BASE_URL ?? 'http://localhost:5080', trace: 'retain-on-failure' },
  projects: [{ name: 'desktop-chromium', use: { ...devices['Desktop Chrome'] } },
             { name: 'mobile-chromium', use: { ...devices['Pixel 7'], viewport: { width: 360, height: 780 } } }],
  webServer: process.env.E2E_BASE_URL ? undefined : {
    command: 'dotnet run --project ../RushDay.Api --configuration Release --no-build',
    url: 'http://localhost:5080/api/health/live', reuseExistingServer: !process.env.CI, timeout: 180_000,
    env: { ASPNETCORE_ENVIRONMENT: 'Development', ASPNETCORE_URLS: 'http://localhost:5080',
           ConnectionStrings__RushDay: process.env.E2E_CONNECTION_STRING ?? 'Host=localhost;Port=5432;Database=rushday_e2e;Username=rushday;Password=rushday',
           Database__MigrateOnStartup: 'true', Database__SeedOnStartup: 'true', Database__BackfillOnStartup: 'true',
           Database__SeedStudentCount: '300', Database__SeedResultsDay: '2026-01-26T09:00:00Z', Demo__Enabled: 'true' } },
})
```

`e2e/fixtures.ts`: `loginAs(page, 'student' | 'lecturer' | 'admin' | { username, password })` reads
`GET /api/public/status` for demo credentials, calls `GET /api/auth/csrf` and `POST /api/auth/login` through
`page.request` (cookies land in the browser context), then `page.goto('/')`; `api(page)` helper performs authenticated
API calls with the CSRF token for setup steps. One journey signs in through the real form.

Journeys: `login.spec.ts` (form sign-in; wrong password copy; `/student` unauthenticated → `/login?returnTo=%2Fstudent`
→ lands on `/student`; sign-out; authenticated `/login` redirects home), `student-results-day.spec.ts` (dashboard
average and classification; results grouped; timetable shows today; ICS download begins with `BEGIN:VCALENDAR`),
`student-enrol.spec.ts` (S000010 filters for CS3099, enrols, button flips, detail shows enrolled, withdraws, count
restored; after admin sets CS3099 capacity to `enrolledCount` a further enrol shows "is full"),
`lecturer-marks.spec.ts` (admin override-enrols two students on CS3099 via API; L00001 enters marks, saves, submits with
the confirmation; inputs lock; admin publishes Spring "now" via the UI; the student signs in and sees the mark on the
results page), `admin-accounts.spec.ts` (create lecturer + provision account, copy the temporary password, sign in as
them → forced change → `/lecturer`; disable an account and its open session lands on `/login` on the next navigation
after the validation interval, set to 0 in this run through `Auth__SecurityStampIntervalMinutes`; audit rows visible),
`guards.spec.ts` (student on `/admin` → `/forbidden`; lecturer on `/student` → `/forbidden`; unknown route; deep-link
refresh on `/student/results`; `/api/does-not-exist` returns JSON 404), `ops-and-story.spec.ts` (`/story` renders chart
cards and table twins without login; `/admin/ops` samples update and the pool meter renders), `mobile.spec.ts` (no
horizontal overflow on login, dashboard, catalogue, marks; bottom tabs; drawer focus trap), `a11y/pages.spec.ts` (axe
on every page in both themes).

`scripts/e2e.ps1`: drops and recreates `rushday_e2e` with `psql` (same discovery as `scripts/reset-db.ps1`),
`dotnet build -c Release`, `npm run build`, `npm run test:e2e` in `src/RushDay.Web`.

## 14. Build and CI integration

- `.gitignore`: add `src/RushDay.Api/wwwroot/`, `src/RushDay.Web/coverage/`, `src/RushDay.Web/playwright-report/`,
  `src/RushDay.Web/test-results/`, `src/RushDay.Web/blob-report/`. `git rm src/RushDay.Api/wwwroot/index.html`.
- `.dockerignore`: add `**/node_modules/`, `**/playwright-report/`, `**/test-results/`, `**/coverage/`, `src/RushDay.Web/e2e/`.
- `Dockerfile`:

```dockerfile
FROM node:24-alpine AS web
WORKDIR /web
COPY src/RushDay.Web/package.json src/RushDay.Web/package-lock.json ./
RUN npm ci --no-audit --no-fund
COPY src/RushDay.Web/ ./
RUN npm run build -- --outDir /web/dist --emptyOutDir

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY global.json Directory.Build.props ./
COPY src/RushDay.Domain/RushDay.Domain.csproj src/RushDay.Domain/
COPY src/RushDay.Infrastructure/RushDay.Infrastructure.csproj src/RushDay.Infrastructure/
COPY src/RushDay.Api/RushDay.Api.csproj src/RushDay.Api/
RUN dotnet restore src/RushDay.Api/RushDay.Api.csproj
COPY src/ src/
COPY --from=web /web/dist src/RushDay.Api/wwwroot
RUN dotnet publish src/RushDay.Api/RushDay.Api.csproj --configuration Release --no-restore --output /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app .
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
USER app
ENTRYPOINT ["dotnet", "RushDay.Api.dll"]
```

- `scripts/dev.ps1`: starts `dotnet watch run --project src/RushDay.Api` and `npm run dev` in two windows and prints
  `http://localhost:5173`.
- `.github/workflows/ci.yml`:

```
web            ubuntu-latest; setup-node@v5 (node 24, cache npm, cache-dependency-path src/RushDay.Web/package-lock.json)
               npm ci → npm run lint → npm run typecheck → npm run test:coverage → node load/summarize.mjs --check
               → npm run build → npm run bundle-budget → npm audit --audit-level=high
               upload-artifact web-dist = src/RushDay.Api/wwwroot
build-and-test needs: web; download web-dist → src/RushDay.Api/wwwroot; setup-dotnet (global.json); restore; build -warnaserror;
               dotnet list package --vulnerable --include-transitive (fail on findings); unit tests; integration tests (Testcontainers);
               docker build; scripts/check-story.ps1 (pwsh); upload TestResults
e2e            needs: web; services postgres:18 (rushday/rushday, pg_isready); setup-dotnet; setup-node; download web-dist;
               dotnet build -c Release; npm ci; npx playwright install --with-deps chromium;
               E2E_CONNECTION_STRING=Host=localhost;Port=5432;Database=rushday;Username=rushday;Password=rushday npx playwright test;
               upload playwright-report (always)
```

All three jobs are required checks; Render's `autoDeployTrigger: checksPass` waits for the commit's checks, so a red
`e2e` blocks the deploy with no further Render change. Bundle budget: entry + `react` + `query` chunks ≤ 220 KB gzip;
the `charts` chunk must not be referenced from the entry graph.
