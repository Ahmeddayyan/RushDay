# RushDay v1 specification: 05. Front end

Scope: stack, configuration, structure, hosting contract, auth flow, data layer, routing, design system, page-by-page
specification, charts, accessibility, tests and build integration for the React SPA in `src/RushDay.Web`.
Decisions D4–D5, D19–D21, D26–D28 and D33 of `00-overview.md` apply. API shapes are those of `02-api.md`; the SPA's
TypeScript types mirror them exactly.

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
| **add** `qrcode` 1.5.4 | | TOTP setup QR code as a `data:` PNG (`QRCode.toDataURL`), loaded only by the MFA setup page |

| Dev dependency | Version | Role |
|---|---|---|
| `typescript` | 6.0.3 | strict; `typescript-eslint` 8.70.1 supports `<6.1.0` |
| `vite` 8.3.1, `@vitejs/plugin-react` 6.1.1, `@tailwindcss/vite` 4.3.3, `tailwindcss` 4.3.3 | | build (Rolldown) and styling |
| `vitest` 5.0.2, `jsdom` 30.1.1, `@testing-library/react` 16.3.3, `@testing-library/jest-dom` 7.0.1, `@testing-library/user-event` 14.6.7 | | component tests |
| **add** `@vitest/coverage-v8` 5.0.2, `@testing-library/dom` 10.4.2, `msw` 2.15.0, `@types/qrcode` 1.5.5 | | coverage, DOM peer, HTTP mocking, types |
| `@playwright/test` 1.63.0, **add** `@axe-core/playwright` 4.13.0 | | end-to-end and accessibility scans |
| `eslint` 10.11.0, `@eslint/js` 10.0.1, `typescript-eslint` 8.70.1, `eslint-plugin-react-hooks` 7.1.1, `eslint-plugin-react-refresh` 0.5.7, `eslint-config-prettier` 10.1.8, `globals` 17.12.0, `prettier` 3.9.9 | | lint and format |
| `@types/react` 19.3.0, `@types/react-dom` 19.3.0, `@types/node` 24.19.0 | | types |

If `npm install` resolves a newer patch of an added package, keep the resolved version and record it in the PR
description; never add a package this table does not list.

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
    manifest: true,
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
returns it through the proxy; the API issues unprefixed cookie names with `SecurePolicy=SameAsRequest` in
Development (`__Host-` names outside it, `02-api.md` section 2.1). Only `/api` is proxied: health and OpenAPI live
under it. `manifest: true` writes `wwwroot/.vite/manifest.json`, which `scripts/bundle-budget.mjs` reads.

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
  scripts/bundle-budget.mjs                    reads wwwroot/.vite/manifest.json; fails when entry+react+query gzip > 220 KB
                                               or when a `charts` or `qrcode` chunk is statically imported from the entry
  public/  favicon.svg  theme-init.js  robots.txt  data/load-results.json   (generated by load/summarize.mjs, committed)
  e2e/     playwright fixtures and specs (section 13)
  src/
    main.tsx                                    createRoot(<StrictMode><Providers><RouterProvider/></Providers>)
    app/  providers.tsx  router.tsx  guards.tsx  AuthProvider.tsx  ReauthDialog.tsx  ErrorBoundary.tsx
    api/  client.ts  problem.ts  keys.ts  queryClient.ts
          endpoints/  auth.ts  public.ts  student.ts  modules.ts  announcements.ts  lecturer.ts  admin.ts  ops.ts
          types/      auth.ts  public.ts  common.ts  student.ts  modules.ts  lecturer.ts  admin.ts  ops.ts  loadResults.ts
    components/
      ui/      Button ButtonLink Input PasswordInput Textarea Select Checkbox Combobox FormField FormError Card Badge
               StatTile Meter Table Pagination SearchInput Dialog AlertDialog DropdownMenu Tooltip Tabs Skeleton
               LoadingRegion EmptyState ErrorState ForbiddenState Spinner VisuallyHidden PageHeader ColdStartNotice
               Countdown SupportLink MarksStatusChip
      layout/  AppShell Sidebar TopBar MobileDrawer BottomTabs UserMenu ThemeToggle SkipLink Footer
    features/
      auth/     LoginPage MfaCodeStep DemoAccounts ChangePasswordPage MfaSetupPage AccountPage schemas.ts routes.tsx
      shared/   AnnouncementsPage NotFoundPage ForbiddenPage AccessibilityPage routes.tsx
      student/  StudentDashboardPage ResultsPage TimetablePage CataloguePage ModuleDetailPage routes.tsx
                components/ ResultsSummaryCard CompletedModulesCard NextClassesCard EnrolmentWindowCard AnnouncementsCard
                            CreditBudget ModuleCard CapacityMeter EnrolButton WithdrawDialog MarkBand TimetableGrid
                            TimetableAgenda ResultsTable
                hooks/ useDashboard useMyEnrolments useCatalogue useModule useEnrol useWithdraw useResultsRelease   ics.ts
      lecturer/ LecturerHomePage MyModulesPage ModulePage (tabs) RosterTab MarksTab ModuleAnnouncementsTab routes.tsx
                components/ RosterTable MarksGrid SubmitDialog GradingProgress
                hooks/ useMyModules useRoster useMarks useSaveMarks useSubmitMarks useModuleAnnouncements useMarksMirror
      admin/    AdminOverviewPage StudentsPage StudentSupportPage ModulesAdminPage ModuleAdminPage (tabs) LecturersPage
                AccountsPage EnrolmentWindowsPage ResultsAdminPage AnnouncementsAdminPage AuditLogPage SettingsPage routes.tsx
                components/ CreateStudentDialog EditStudentDialog MarkStudentLeftDialog CreateLecturerDialog
                            EditLecturerDialog MarkLecturerLeftDialog ProvisionAccountDialog TemporaryPasswordDialog
                            AccountRowActions ResetMfaDialog ModuleEditDialog LecturerAssignmentEditor TrimToCapacityDialog
                            ModuleRosterTable ModuleMarksTable WindowEditor SubmissionProgressTable PublishDialog
                            RescheduleDialog CancelPublicationDialog UnpublishDialog ReturnToDraftDialog CorrectMarkDialog
                            OverrideEnrolDialog OverrideWithdrawDialog AnnouncementDialog AuditFilters AuditRow
                hooks/ one per endpoint group
      ops/      OpsPage routes.tsx
                components/ HealthSummary LiveTiles PoolMeter RequestRateChart LatencyChart CountersPanel
                            BackfillsTable DataQualityPanel TechnicalDetails
                charts/ EnrolmentRushChart DashboardKneeCharts ResultsDayTiles LoginStormTiles ChartCard ChartTable
                        Glossary MachineBanner useLoadResults.ts
      story/    StoryPage routes.tsx
    lib/  cn.ts  format.ts  theme.ts  moduleCode.ts  returnTo.ts  useDocumentTitle.ts  useDebouncedValue.ts
          useCountdown.ts  useDirtyForm.ts  download.ts  broadcast.ts  qr.ts  contrast.ts
    styles/app.css
    test/  setup.ts  render.tsx  server.ts  factories.ts  handlers/ auth.ts public.ts student.ts modules.ts lecturer.ts admin.ts ops.ts
```

Types: `api/types/common.ts` (S5) holds every shared shape of `02-api.md` section 7 (`Me`, `MfaChallenge`,
`LoginResponse`, `TimetableEntry`, `WindowInfo`, `PublicationBrief`, `PublicationInfo`, `Lecturer`, `ModuleSummary`,
`ModuleDetail`, `GradeResult`, `AnnouncementView`, `MarksStatus`, `AuditEventView`, `AccountView`, `MyEnrolment`,
`Paged<T>`); the area files (`student.ts`, `lecturer.ts`, `admin.ts`, ...) hold only their own routes' request and response
shapes. Components used by more than one area (`MarksStatusChip`, `Countdown`, `SupportLink`) live in `components/ui`.
So stages S7–S10 never import one another's files.

Migration of the committed scaffold (stage S3): `src/lib/api.ts` → `src/api/client.ts` (extended per section 5),
`src/lib/errors.ts` → `src/api/problem.ts`, `src/lib/queryClient.ts` → `src/api/queryClient.ts`,
`src/lib/usePageTitle.ts` → `src/lib/useDocumentTitle.ts`, `src/index.css` → `src/styles/app.css`,
`src/features/auth/{AuthContext,AuthProvider}.ts(x)` → `src/app/AuthProvider.tsx`, `src/features/auth/RequireRole.tsx`
→ `src/app/guards.tsx`, `src/features/auth/{loginSchema,loginErrorMessage,types}.ts` → `features/auth/schemas.ts`,
`api/problem.ts` and `api/types/auth.ts`, `src/features/theme/*` → `src/lib/theme.ts` and
`components/layout/ThemeToggle.tsx`, `src/components/layout/{Header,MobileNav,NavList,SessionControls}.tsx` →
`TopBar`, `MobileDrawer`, `Sidebar` and `UserMenu`, `src/app/{RootLayout,AuthLayout,navigation}.ts(x)` →
`components/layout/AppShell.tsx` and `Sidebar.tsx`, `src/test/{setupTests,renderWithProviders,mockFetch}.ts(x)` →
`src/test/{setup.ts,render.tsx}` with MSW; `src/pages/*` are deleted (their pages are rebuilt in `features/*`);
`src/components/ui/*` and `src/lib/cn.ts` are kept and extended; existing unit tests move with their files.
`tests/e2e/*` and the scaffold's `playwright.config.ts` stay untouched until S12 moves them to `e2e/`.

Routing and code splitting: each area's `routes.tsx` exports a `RouteObject[]` and is imported **statically** by
`app/router.tsx` (paths are known up front); every page route in it uses React Router's `lazy: () =>
import('./XPage')` where the page module exports `Component` (and optionally `ErrorBoundary`), so page code is split
per route and the `charts` chunk is reached only from the ops and story pages. React Router 7's `lazy` cannot return
`path`, `index` or `children`, which is why the route objects themselves are not lazy. S3 writes `app/router.tsx`
once with the seven imports; stages S7–S10 only fill their own `routes.tsx` and never edit a shared file.

## 4. Hosting from the API (contract owned by the backend, `src/RushDay.Api/Hosting/SpaHosting.cs`)

`app.UseRushDaySpa()` runs after `SecurityHeadersMiddleware` and before endpoint mapping;
`app.MapRushDaySpaFallbacks()` runs after every `Map*`:

1. `UseDefaultFiles()` + `UseStaticFiles` with `OnPrepareResponse`: `/assets/*` → `Cache-Control: public, max-age=31536000, immutable`;
   everything else (`/index.html`, `/theme-init.js`, `/favicon.svg`, `/robots.txt`, `/data/*.json`) → `Cache-Control: no-cache`.
2. `app.MapFallback("/api/{**rest}", ...).AllowAnonymous()` → 404 `urn:rushday:not-found` ProblemDetails, so an unknown
   API path is JSON, never `index.html`, and never 401 for an anonymous caller.
3. `app.MapFallbackToFile("index.html").AllowAnonymous()` for every other unmatched GET (deep links such as
   `/student/results`); without `AllowAnonymous` the fallback authorization policy would answer 401 to an anonymous
   visitor of `/login`.
4. When `wwwroot/index.html` is missing (a local API run without a web build), `GET /` and every non-`/api` path return
   `503 text/plain` from an `AllowAnonymous` endpoint: `Front end not built: run "npm run build" in src/RushDay.Web or use "npm run dev".`

`IndexEndpointTests` (S2) asserts anonymous `GET /` → 200 `text/html` (or the 503 text without a build) and anonymous
`GET /api/nope` → 404 `application/problem+json`.

Constraints the SPA places on the backend, all specified in `02-api.md` and `03-security.md`: CSP exactly as given
(`style-src 'unsafe-inline'`, strict scripts, `img-src 'self' data:` for the QR code); 401/403 as JSON ProblemDetails,
never redirects; `csrfToken` in `/auth/me`, `/auth/login` and `/auth/mfa/verify`; `GET /api/auth/csrf` for the
anonymous form; `SameSite=Strict` cookies.

## 5. Authentication flow in the SPA

### 5.1 Session state machine (`app/AuthProvider.tsx`)

```
status: 'booting' | 'anonymous' | 'mfaPending' | 'authenticated'
user:   Me | null
csrf:   string | null       // request token, memory only, never persisted
```

1. On mount: `GET /api/auth/me`. 200 → `authenticated`, store `user` and `user.csrfToken`. 401 → `anonymous`, then
   `GET /api/auth/csrf` → store `csrf`. Network error or 5xx → stay `booting`, retry with backoff 1, 2, 4, 8 s up to 75 s
   total; after 3 s the splash shows `ColdStartNotice` ("Waking the server. On the free tier this can take up to a
   minute."); after 75 s an `ErrorState` with Retry.
2. `login(username, password)` → `POST /api/auth/login` with `X-CSRF-TOKEN: csrf`. `Me` → `authenticated`, store the
   new `csrfToken` (the anonymous token is discarded). `MfaChallenge` (`mfaRequired: true`) → `mfaPending`, store its
   `csrfToken`; the login page shows `MfaCodeStep`. `verifyMfa(code)` → `POST /api/auth/mfa/verify` → `Me` →
   `authenticated`; a 401 there keeps `mfaPending` with the copy "That code didn't work. Check the time on your phone
   and try the newest code."; "Start again" returns to `anonymous`. Errors surface in the form.
3. `logout()` → `POST /api/auth/logout` → `queryClient.clear()`, `anonymous`, fetch a fresh anonymous token, navigate to
   `/login`; `BroadcastChannel('rushday.auth')` message `{ type: 'logout' }` makes other tabs do the same. The account
   page states that signing out ends the account's sessions on every device (the server rotates the security stamp).
4. Any `apiFetch` receiving 401 (except `/auth/me`, `/auth/login`, `/auth/mfa/verify`) calls the client's
   `onUnauthenticated` hook. When a form has registered unsaved work through `useDirtyForm(isDirty)` (the marks grid,
   the announcement and settings forms), the provider opens `ReauthDialog` instead of navigating: the username is
   fixed, the user types the password (and, if the answer is `mfaRequired`, a code); success re-issues the CSRF token
   through the normal login completion and the dialog closes with "Signed in again. Press Save to keep your changes.";
   Cancel performs the normal path. With no dirty form: the provider goes `anonymous`, clears the cache, shows one
   toast "Your session has ended. Sign in again." and the guard redirects with `returnTo`.
5. A 400 `urn:rushday:antiforgery` triggers one `GET /api/auth/me` (authenticated) or `GET /api/auth/csrf` (anonymous)
   and one retry of the original request; a second failure surfaces as an error.
6. Gates, in this order, on every guarded route: `user.mustChangePassword` → `/account/password?required=1` until
   `POST /api/auth/change-password` succeeds; then `user.mfaSetupRequired` → `/account/mfa?required=1` until
   `POST /api/auth/mfa/enable` succeeds; after each, `/auth/me` is refetched and the original destination restored.

### 5.2 Guards (`app/guards.tsx`)

- `RootRedirect` (`/`): `booting` → splash; `anonymous` or `mfaPending` → `/login`; else → role home (`/student`, `/lecturer`, `/admin`).
- `PublicOnly` (`/login`): authenticated users go to a safe `returnTo`, else role home.
- `RequireAuth`: `booting` → splash; `anonymous` → `<Navigate to={'/login?returnTo=' + encodeURIComponent(pathname + search)} replace />`;
  then the two gates of section 5.1 step 6.
- `RequireRole roles`: wrong role → `<Navigate to="/forbidden" replace />` (a real page Playwright can assert).
- `returnTo` (`lib/returnTo.ts`): rejected when it contains a backslash or a control character; otherwise
  `const u = new URL(returnTo, location.origin)` must satisfy `u.origin === location.origin`,
  `u.pathname.startsWith('/')` and `!u.pathname.startsWith('/login')`; the SPA navigates only through the router
  (`navigate(u.pathname + u.search + u.hash)`), never `location.assign`.

### 5.3 Fetch rules (`api/client.ts`)

```ts
configureClient(hooks: { getCsrfToken(): string | null; refreshCsrfToken(): Promise<void>; onUnauthenticated(path: string): void }): void
apiFetch<T>(path: string, init?: { method?: 'GET'|'POST'|'PUT'|'DELETE'; body?: unknown; signal?: AbortSignal; expect?: 'json'|'blob'|'void' }): Promise<T>
```

- `AuthProvider` calls `configureClient` once on mount; tests call it with fakes. The module keeps no other state.
- `credentials: 'same-origin'`, `Accept: application/json`; JSON bodies get `Content-Type: application/json`.
- Non-GET requests carry `X-CSRF-TOKEN` from `getCsrfToken()`. Errors throw `ApiError { status, problem, kind,
  retryAfterSeconds, headers }` where `kind` is the slug after the last colon of `problem.type` (`module-full`) and
  `retryAfterSeconds` is parsed from `Retry-After`; a failed `fetch` throws `NetworkError` (status 0). 204 resolves
  `undefined`; `expect: 'blob'` returns `{ blob, headers }` for CSV, JSON export and ICS downloads.
- Mutations are never retried; queries retry only on network errors and 5xx (section 6).

## 6. Data layer

### 6.1 QueryClient defaults (`api/queryClient.ts`)

```ts
queries: { staleTime: 30_000, gcTime: 5 * 60_000,
           refetchOnWindowFocus: false /* 20,000 tabs refocusing at 09:00 is a stampede */,
           retry: (n, err) => !(err instanceof ApiError && err.status > 0 && err.status < 500) && n < 2,   // never 4xx, so never 429
           retryDelay: (n, err) => (err instanceof ApiError && err.retryAfterSeconds
                                      ? err.retryAfterSeconds * 1000
                                      : Math.min(1000 * 2 ** n, 8000)) + Math.random() * 1000 },          // jitter: shed clients never retry in lockstep
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
['modules', code]                            GET /api/modules/{code}               staleTime 5s; refetchInterval 10s while enrolment is open and the page is visible
['announcements']                            GET /api/announcements                staleTime 60s
['lecturer','modules']                       GET /api/lecturer/modules
['lecturer','module', code, 'roster', { q, page, pageSize }]   placeholderData: keepPreviousData
['lecturer','module', code, 'marks', { q, page }]              placeholderData: keepPreviousData
['lecturer','module', code, 'announcements']
['admin','overview']                         refetchInterval 30s while mounted
['admin','settings']  ['admin','windows']  ['admin','results', academicYear, semester]  ['admin','lecturers', q]
['admin','students', params]  ['admin','accounts', params]  ['admin','audit', params]    placeholderData: keepPreviousData
['admin','student', studentNumber]  ['admin','modules', includeInactive]  ['admin','announcements']
['admin','module', code, 'roster', params]  ['admin','module', code, 'marks', params]      placeholderData: keepPreviousData
['admin','ops','metrics']                    refetchInterval 5s, refetchIntervalInBackground false
['load-results']                             GET /data/load-results.json           staleTime Infinity
```

### 6.3 Mutations

`useEnrol(code)` (`POST /api/me/enrolments`) is **not optimistic**: a student in the rush must never see "Enrolled"
for a place the server then refuses. While pending, `EnrolButton` shows "Enrolling…" (disabled, `aria-busy`,
spinner, width kept). On 201: `setQueryData(['modules', code])` and `setQueryData(['modules','catalogue'])` with the
returned `placesRemaining` (so the card does not snap back to the 30-second server cache), append the full
`MyEnrolment` row to `['student','enrolments']` (built from the catalogue item: `title`, `credits`, `semester` from
`ModuleSummary`, `academicYear` from `['public','status']`, `status: 'active'`, `enrolledAt` from the response,
`withdrawnAt: null`, `canWithdraw: true`, `withdrawBlockedReason: null`, `withdrawalDeadlineAt` from the module), toast
"You're in: {code}. {placesRemaining} places left." and invalidate `['student','enrolments']` and
`['student','dashboard']` (not the catalogue). On 409 `module-full`: the button becomes "Full" with the caption
"Filled while you were enrolling", `['modules', code]` is refetched and the meter caption reads "Filled {time,
relative}". On 429, 503, `timeout` or a network error: the button stays "Enrol", disabled for `retryAfterSeconds`
(2 s when absent) with the caption "The portal is busy. Try again in {n}s." counting down, then enabled; mutations are
never retried automatically. Any other error toasts `describeProblem(err)`.

`useWithdraw(code)` stays optimistic behind `WithdrawDialog` (an `AlertDialog`, destructive confirm "Withdraw"):
description "Withdraw from {code}? Your place is released immediately." then, when the window is open, "You can
re-enrol while places remain until {closesAt}." or otherwise "Enrolment for {semester} closed on {closesAt}, so you
won't be able to re-enrol yourself.", then "Withdrawal deadline: {withdrawalDeadlineAt}." `onMutate` snapshots and sets
the row to `status: 'withdrawn'`; `onError` restores and toasts; `onSettled` invalidates `['student','enrolments']`,
`['student','dashboard']`, `['modules', code]`, `['modules','catalogue']`.

Lecturer mark saves are not optimistic (the server may reject rows with `stale-mark` or `not-enrolled-students`); the
grid shows saving state and applies the returned rows. Admin mutations invalidate their group key and
`['admin','overview']`.

### 6.4 ProblemDetails mapping (`api/problem.ts`)

`describeProblem(err): { title, message, action?: 'retry' | 'login' | 'wait' }` resolves by slug first, then status, then
the generic rows. `{support}` is `SupportLink` text from `lib/format.ts` `supportLink(status)`: "contact the academic
office at {email}" / "contact the academic office ({url})" when `institution.support` is set, else "contact the
academic office". Every slug of `02-api.md` section 6 has a row (`api/problem.test.ts` iterates the catalogue; 63 slugs
since the S6 review added `principal-left`). Two server rules from the same review that the SPA reflects: `Pagination`
never offers a page past `page x pageSize = 10,000` (the API answers 400 `validation` with `errors.page` beyond it; the
audit log's filters and CSV export reach older rows), and a correction dialog maps the 400 `validation` a no-change
correction answers (`errors.mark`) onto its mark field:

| Slug | Status | Copy |
|---|---|---|
| `validation` | 400 | field errors mapped to form fields via `setError`; unmapped keys to `FormError` |
| `antiforgery` | 400 | silent refresh and one retry; if it fails again "Your page is out of date. Reload and try again." |
| `invalid-current-password` | 400 | "Your current password is incorrect." |
| `weak-password` | 400 | list from `errors.newPassword` (`same-as-current` → "Choose a password different from your current one.") |
| `invalid-mfa-code` | 400 | "That code didn't work. Check the time on your phone and try the newest code." |
| `unauthenticated` | 401 | handled by the provider (section 5.1 step 4) |
| `invalid-credentials` | 401 | "Incorrect username or password." (login adds the lockout hint, section 10) |
| `forbidden`, `not-your-module` | 403 | `ForbiddenState` |
| `not-module-leader` | 403 | "Only the module leader, {leader}, can submit these marks." |
| `password-change-required` | 403 | redirect to `/account/password?required=1` |
| `mfa-setup-required` | 403 | redirect to `/account/mfa?required=1` |
| `not-found`, `module-not-found`, `student-not-found`, `lecturer-not-found`, `account-not-found`, `window-not-found`, `publication-not-found`, `announcement-not-found`, `grade-not-found` | 404 | `EmptyState` "That page or record doesn't exist." |
| `not-enrolled` | 404 | "You're not enrolled on {code}." |
| `method-not-allowed`, `unsupported-media-type` | 405, 415 | "Something went wrong on our side. Reference {traceId}." |
| `payload-too-large` | 413 | "That's too much to send at once. Save fewer changes and try again." |
| `already-enrolled` | 409 | "You're already enrolled on {code}." |
| `module-full` | 409 | "{code} is full." |
| `module-inactive` | 409 | "{code} is no longer running." |
| `enrolment-window-closed` | 409 | from extensions: `opensAt > now` → "Enrolment for {semester} opens {opensAt}."; `closesAt <= now` → "Enrolment for {semester} closed on {closesAt}."; both null → "Enrolment dates for {semester} have not been announced yet." |
| `withdrawal-deadline-passed` | 409 | "The withdrawal deadline for {code} was {withdrawalDeadlineAt}. To withdraw now, {support}." |
| `results-exist` | 409 | "{code} already has a submitted or published mark, so it can't be changed here. {support, capitalised}." |
| `student-left` | 409 | "This student has left, so they can't be enrolled." (own session: "Your record is marked as left. {support}.") |
| `module-locked` | 409 | lecturer: "Marks for this module are submitted and can't be changed. Ask the academic office to return it to draft."; admin: "Students can already see these marks. Unpublish the semester or correct single marks."; on an enrolment (S6 review E1; `POST /api/me/enrolments` and the admin override): student "Marks for {code} have already been submitted this year, so you can't join it now. {support, capitalised}."; admin "Marks for {code} are already submitted this year. Return the module to draft before enrolling anyone." |
| `module-not-submitted` | 409 | "This module is still in draft; there's nothing to return or correct yet." |
| `already-submitted` | 409 | "This module has already been submitted." |
| `nothing-to-submit` | 409 | "No students are enrolled on {code} this year, so there's nothing to submit." |
| `stale-mark` | 409 | "Someone else changed {n} marks. Review the highlighted rows and save again." |
| `nothing-to-publish` | 409 | "No submitted modules are ready to publish for {semester} {academicYear}. Lecturers submit modules from their Marks page." |
| `publication-live` | 409 | "These results are already live. Unpublish them instead." |
| `publication-scheduled` | 409 | "These results are not live yet. Cancel the scheduled publication instead." |
| `username-taken` | 409 | "That username is already in use." |
| `principal-has-account` | 409 | "{number} already has an account." |
| `principal-left` | 409 | "{number} has left, so they can't have an account." (S6 review E5; provisioning and enabling) |
| `module-code-taken`, `student-number-taken`, `staff-number-taken` | 409 | "{value} already exists." |
| `window-exists` | 409 | "A window for {academicYear} {semester} already exists. Edit it instead." |
| `demo-account` | 409 | "Demo accounts are read-only, so the demo stays usable for the next visitor." |
| `mfa-already-enabled` | 409 | "Two-step verification is already on for this account." |
| `credit-limit-exceeded` | 422 | "That would take you over {limit} credits for {semester}." |
| `marks-incomplete` | 422 | "{n} students have no mark or outcome yet." (dialog lists `missing`) |
| `not-enrolled-students` | 422 | "{n} students are no longer enrolled: {list}." |
| `capacity-below-enrolled` | 422 | "Capacity can't go below the {enrolledCount} students already enrolled." |
| `semester-change-with-enrolments` | 422 | "The semester can't change once students have enrolled ({enrolledCount} enrolments in all years). Create a new module instead." |
| `publish-too-far-ahead` | 422 | "Choose a date within the next 90 days." |
| `invalid-lecturer-assignment` | 422 | "Assign exactly one leader, list each lecturer once, and don't assign lecturers who have left." |
| `window-dates-invalid` | 422 | "Opens must be before closes, and the withdrawal deadline can't be before closes." |
| `self-lockout` | 422 | "You can't lock or disable your own account." |
| `role-principal-mismatch` | 422 | "A Student account needs a student number, a Lecturer account a staff number, an Admin account neither." |
| `rate-limited` | 429 | "Too many attempts. Try again in {retryAfterSeconds}s." |
| `internal-error` | 500 | "Something went wrong on our side. Reference {traceId}." |
| `server-busy`, `timeout` | 503 | "The portal is very busy right now. Try again in a moment." + Retry, disabled for `retryAfterSeconds` with a visible count |
| anything else 4xx | | `problem.detail ?? problem.title` |
| anything else 5xx | | "Something went wrong on our side. Reference {traceId}." |

## 7. Routing and navigation

React Router 7 `createBrowserRouter`; page modules are lazy (section 3) so a student never downloads admin code.
Every route sets its title through `useDocumentTitle('Results · RushDay')` and, on navigation, moves focus to the
page `<h1 tabIndex={-1}>`.

| Path | Guard | Page |
|---|---|---|
| `/` | none | `RootRedirect` |
| `/login` | `PublicOnly` | `LoginPage` (`?returnTo=` honoured; second step `MfaCodeStep` when `mfaPending`) |
| `/story` | none | `StoryPage` (public) |
| `/accessibility` | none | `AccessibilityPage` (public statement) |
| `/forbidden`, `*` | none | `ForbiddenPage`, `NotFoundPage` |
| `/account`, `/account/password`, `/account/mfa` | `RequireAuth` | `AccountPage`, `ChangePasswordPage` (`?required=1` hides navigation, keeps Sign out), `MfaSetupPage` (`?required=1` likewise) |
| `/announcements` | `RequireAuth` | `AnnouncementsPage` |
| `/student`, `/student/results`, `/student/timetable`, `/student/modules`, `/student/modules/:code` | `RequireRole Student` | student pages; catalogue filters in the URL (`?q=&semester=&level=&dept=&availability=&mine=`) |
| `/lecturer`, `/lecturer/modules`, `/lecturer/modules/:code` (roster), `/lecturer/modules/:code/marks`, `/lecturer/modules/:code/announcements` | `RequireRole Lecturer` | lecturer pages; marks page and search in the URL (`?page=&q=`) |
| `/admin`, `/admin/students`, `/admin/students/:studentNumber`, `/admin/modules`, `/admin/modules/:code`, `/admin/modules/:code/roster`, `/admin/modules/:code/marks`, `/admin/lecturers`, `/admin/accounts`, `/admin/enrolment`, `/admin/results`, `/admin/announcements`, `/admin/audit`, `/admin/ops`, `/admin/settings` | `RequireRole Admin` | admin pages; list filters in the URL |

Navigation (sidebar ≥ 1024 px, drawer below; students also get `BottomTabs` below 768 px):

- Student: Home, Results, Timetable, Modules, Announcements (bottom tabs: Home, Results, Timetable, Modules).
- Lecturer: Home, My modules, Announcements.
- Admin: Overview, Students, Modules, Lecturers, Accounts, Enrolment windows, Results, Announcements, Audit log,
  Operations, Settings.
- All: `UserMenu` (display name, role badge, Account, privacy notice link when `Branding:PrivacyNoticeUrl` is set,
  Sign out), `ThemeToggle`, `Footer` "Deployed {commit} · {institution} · Accessibility · About RushDay" from
  `['index']` and `['public','status']`; "About RushDay" links to `/story` (the GitHub link lives on `/story`).

## 8. Static load-results data

`GET /data/load-results.json` (from `public/`, generated by `load/summarize.mjs` from `load/results/*.json` and
`load/results/runs.json`):

```ts
interface LoadResults { generatedAt: string; machine: string; runs: LoadRun[] }
interface LoadRun {
  id: string; scenario: 'enrolment-rush' | 'results-day' | 'dashboard-knee' | 'login-storm'; version: 'v0' | 'v1';
  label: string; ranAt: string; source: string; targetRate?: number; mode?: 'guard' | 'spray'; notes?: string;
  metrics: { requests: number; failedRate: number; p50Ms: number; p95Ms: number; p99Ms?: number; maxMs: number;
             achievedRate?: number; droppedIterations?: number;
             accepted?: number; rejectedFull?: number; rejectedOther?: number; shed?: number; errored?: number;
             capacity?: number; oversold?: number; loginsOk?: number; loginsRateLimited?: number }
}
```

The summariser reads `metrics.http_req_duration.{med,"p(95)","p(99)",max}` (`p99Ms` is optional for every run and
emitted only when `p(99)` exists; the committed v0 enrolment-rush and results-day summaries lack it),
`http_req_failed.value`, `http_reqs.{count,rate}`, `dropped_iterations.count` and the custom counters. Until v1 runs
are committed the charts render the "after" series as "not yet measured".

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
  --text: #1a1d21; --muted: #5f6672; --subtle: #667080;
  --primary: #2f5bea; --primary-hover: #244bcc; --primary-foreground: #ffffff; --primary-soft: #eef2ff;
  --success: #17703d; --success-soft: #e8f5ec; --warning: #7a5f14; --warning-soft: #fbf3dc;
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

Contrast (computed with the WCAG 2.x formula; every pair used for text is ≥ 4.5:1):

| Pair | Light | Dark |
|---|---|---|
| `--text` on `--surface` / `--background` | ≥ 15:1 | ≥ 13:1 |
| `--muted` on `--surface` / `--background` | 5.8:1 / 5.3:1 | 6.8:1 (on `--surface`) |
| `--subtle` on `--surface` / `--surface-2` / `--background` | 5.0:1 / 4.8:1 / 4.6:1 | 4.8:1 (on `--surface`) |
| `--success` on `--success-soft` / `--surface` | 5.5:1 / 6.1:1 | 7.0:1 / 8.5:1 |
| `--warning` on `--warning-soft` / `--surface` | 5.5:1 / 6.0:1 | 8.2:1 / 9.9:1 |
| `--danger` on `--danger-soft` / `--surface` | 5.5:1 / 6.6:1 | 5.7:1 / 6.2:1 |
| `--info` on `--info-soft` / `--surface` | 4.9:1 / 5.5:1 | 6.5:1 / 7.3:1 |
| `--primary-foreground` on `--primary` | 5.5:1 | 7.9:1 |

`lib/contrast.ts` exports the WCAG relative-luminance ratio; `lib/contrast.test.ts` parses the token values from
`styles/app.css` for both themes and fails when any pair used by `Badge`, `StatTile` captions or `FormField` hints
(the table above) falls below 4.5:1. `--border`, `--chart-*` and focus rings are held to 3:1 against their
backgrounds. Status colours are always paired with an icon and a text label. `ThemeProvider` (`lib/theme.ts`):
`'system' | 'light' | 'dark'` in `localStorage['rushday.theme']` (try/catch), applied as `data-theme` (removed for
system) and mirrored to `<meta name="theme-color">`.

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
- Tables: `<table>` with `<caption>` (visually hidden when a heading is adjacent); sortable columns are
  `<th scope="col" aria-sort="ascending|descending|none">` containing `<button aria-label="Sort by {column}, currently
  {state}">` (`aria-sort` is valid only on the header cell); below 640 px rows stack as cards with `data-label`
  headers; opt-in `scroll="x"` for audit and accounts tables on tablets with a fade affordance.
- `Combobox`: ARIA 1.2 combobox (input + `listbox`, `aria-activedescendant`, typeahead against a server `q`), with an
  empty-state slot.
- Dialogs: Radix `Dialog` (focus trap, Escape, focus restore), max 480 px, full-screen sheet below 640 px; destructive
  or irreversible confirmations are always `AlertDialog` with the consequence in the description.
- Empty/Error/Forbidden states: icon (`aria-hidden`), one-sentence heading, one-sentence explanation, one action.
- Loading: `LoadingRegion` wraps skeletons in a region with `aria-busy="true"` and one visually hidden "Loading
  {page}" sentence.
- Badges/chips: soft background + text + icon; never colour alone (Draft with a pencil, Submitted with a clock,
  Scheduled with a calendar, Published with a check, Amended with a pen-line icon).
- `StatTile`: primary label in plain words, value, optional caption (the technical metric name in `font-mono`) and
  optional `description` shown in a `Tooltip` and as `aria-describedby` text.
- `Countdown` (`components/ui`): renders the remaining time from `lib/useCountdown.ts`; the ticking digits are
  `aria-hidden`; a visually hidden static sentence "Results publish at {absolute instant, zone}" is always present;
  one `role="status"` sentence ("About {n} minutes to go") updates at most once per minute.
- Responsive to 360 px: single column below 768; timetable grid → agenda; chart cards stack; marks grid → card rows
  with a sticky bottom action bar whose height is mirrored in the scroll container's `scroll-padding-bottom`, so a
  focused input is never hidden under it (WCAG 2.4.11); filter rows wrap; no horizontal page scroll (Playwright
  asserts `scrollWidth <= innerWidth` on the mobile project).

## 10. Page-by-page specification

Conventions for every page: `PageHeader` (`<h1 tabIndex={-1}>`, optional description, primary action slot); loading
shows `Skeleton` shapes matching the final layout inside `LoadingRegion`; refetches keep the old content at 60%
opacity; `ErrorState` with `describeProblem` copy and Retry; 403 → `ForbiddenState`; every empty state names what
fills the page and links to the action. Dates are formatted in the institution zone from `['public','status']`
(`lib/format.ts`), absolute always, with the zone named for windows and publication instants and the relative form in
a tooltip. Wherever copy says "contact the academic office" it renders `SupportLink`.

### `/login` (public)

- The form, the RushDay wordmark and the footer render immediately and never wait for `['public','status']`. The
  institution name shows a short skeleton and then the value; the `DemoAccounts` panel and the results line appear only
  when status resolves with data; if status fails (cold start, 503) nothing extra renders and the form still works
  (`LoginPage.test.tsx`: "status 503 still renders a usable form").
- Strapline: when `status.demo` is not null, directly under the wordmark, the story sentence pair verbatim in a
  `<blockquote>` with `<cite>Ahmed Ayyan, creator of RushDay</cite>`, and a link "How it holds up under load" →
  `/story`. When `status.demo` is null (or status has not loaded), the strapline is "{institution.name} student portal"
  and the same blockquote appears **below the form** under the heading "About RushDay" with the `/story` link. The
  sentence pair is a string literal in `LoginPage.tsx` so `scripts/check-story.ps1` finds it in the source.
- Form (react-hook-form + zod `loginSchema`: username trimmed 1..64, password 1..128): username `Input` labelled
  "Student number, staff number or admin username", placeholder `S000001`, `autocomplete="username"`, no case transform
  (the server compares case-insensitively); `PasswordInput` (`autocomplete="current-password"`, show/hide toggle with
  `aria-pressed`); primary "Sign in" (`aria-busy` while pending); `FormError` `role="alert"`: 401 "Incorrect username or
  password."; from the third consecutive 401 in this page's memory a second sentence "Still stuck? Repeated failed
  attempts can pause sign-in for up to 15 minutes. To reset your password, {support}."; 429 "Too many attempts. Try
  again in {n}s."; 503 busy copy.
- `MfaCodeStep` (status `mfaPending`): heading "Enter your verification code", one `Input` (`inputMode="numeric"`,
  `autocomplete="one-time-code"`, `pattern="[0-9]{6}"`, `maxLength=6`), "Verify", "Start again"; copy "Open your
  authenticator app and type the 6-digit code for RushDay."; lost device: "Lost your phone? Ask another administrator to
  reset your two-step verification, or {support}."
- `DemoAccounts` panel only when `status.demo` is not null: three rows (Student `S000001`, Lecturer `L00001`,
  Administrator `admin`) with the password in `<code>`, the static hint, and a "Use" button that fills the form and
  focuses Sign in; the note "Demo data: 20,000 synthetic students, no real people."
- Results line, from status: when `nextPublication` exists, "{Semester} {academicYear} results publish {date} at
  {time} ({zone})" with a `Countdown`; else when `latestPublication` is live, "{Semester} {academicYear} results
  published {date} at {time} ({zone})"; else nothing. The countdown uses `useCountdown` (section 10 `/student`); at zero
  it invalidates `['public','status']` once after the same random 0–30 s delay.
- Cold start: after 3 s pending, `ColdStartNotice`. After success: `returnTo` or role home; the section 5.1 gates apply.
- Footer: deployed commit; privacy note "Only your session cookie and your theme choice are stored. No third-party
  scripts."; privacy notice link when `Branding:PrivacyNoticeUrl` is set; links to `/accessibility` and "About RushDay"
  (`/story`); "Forgotten your password? {support}." No registration link.
- Booting splash while `/auth/me` is unresolved so a signed-in user never sees the form flash.

### `/story` (public)

The story sentence pair in the first person as the opening (a string literal in `StoryPage.tsx`, also grepped by
`scripts/check-story.ps1`), then the three-act narrative (baseline, break it, fix it), then `MachineBanner` "Measured
on {machine}. Live figures for this server are on the Operations page for administrators.", then the chart cards of
section 11, then `Glossary` (a card defining "requests per second", "p95: 19 of 20 requests were faster than this",
"oversold: more students given a place than there are places", "turned away: asked to try again straight away instead
of being left waiting"), then "See it running" → `/login` and the GitHub link. Data: `['load-results']`, `['index']`.

### `/accessibility` (public)

Statement page: conformance target (WCAG 2.2 AA), how it is tested, known limitations (charts have table twins; PDF
export is browser print), contact route (`SupportLink`), date of the statement.

### `/forbidden`, `*`

`ForbiddenPage`: "You don't have access to that page", who it is for, button to role home or Sign in.
`NotFoundPage`: same shape.

### `/account`, `/account/password`, `/account/mfa`

- `AccountPage`: profile card (display name, username, role badge, student or staff number, programme and year for
  students from `['student','dashboard']`); "Change password" link (hidden for `isDemo` with the note "Demo accounts
  can't change their password"); "Two-step verification" card: on / off state; for `Admin` "Required for
  administrators" and a "Set up" link when off; for `Lecturer` an optional "Set up" link; not shown to students;
  "Sign out everywhere" explanation ("Signing out ends your sessions on every device."); for students "Download my
  data" (`GET /api/me/export.json` through `lib/download.ts`, label "Download my data (JSON), downloads a file");
  Appearance `ThemeToggle` (System / Light / Dark, `role="radiogroup"`).
- `ChangePasswordPage`: for `isDemo` users the form is replaced by "Demo accounts can't change their password." Else
  current, new, confirm (zod: 12..128, must differ from current; server policy errors mapped); live checklist under the
  new-password field (`aria-live="polite"`); `?required=1` banner "Your administrator set a temporary password. Choose
  a new one to continue." with navigation hidden; success → toast, refetch `['auth','me']`, continue to the next gate or
  the stored `returnTo`.
- `MfaSetupPage`: `?required=1` banner "Administrators must use two-step verification. Set it up to continue." with
  navigation hidden. Step 1 "Scan this code with an authenticator app (Microsoft Authenticator, Google Authenticator or
  any TOTP app)": calls `POST /api/auth/mfa/setup` once on mount (a ref guards against React StrictMode's second effect run, which would reset the key), shows the QR code (`lib/qr.ts` dynamically imports
  `qrcode` and returns a `data:` PNG for `<img alt="QR code for your authenticator app">`) and the `sharedKey` in
  `<code>` with Copy ("Can't scan? Type this key instead."). Step 2 "Enter the 6-digit code it shows" → `POST
  /api/auth/mfa/enable`; success → toast "Two-step verification is on." and continue. `demo-account` → "Demo accounts
  can't enable two-step verification."

### `/announcements` (any role)

Cards, pinned first (pin icon + "Pinned"), title, scope badge (University / module code), author, absolute date with
relative in tooltip, body in `white-space: pre-line`. Empty: "No announcements yet." Data: `['announcements']`.

### `/student` (dashboard)

- Header: time-of-day greeting with first name, student number, programme, year, "{academicYear}".
- Grid (2 columns ≥ 1024):
  - `ResultsSummaryCard`: hero "Average so far" (weighted average, one decimal) with a chip "Indicative band: {band}"
    and "{n} modules graded"; `<details>` "How this is calculated" (credit-weighted, absences and deferrals excluded,
    and "Your degree classification is decided by the exam board on your whole programme."); link "All results". When
    `nextPublication` exists: "{Semester} {year} results publish {date} at {time} ({zone})" with a `Countdown`. When
    nothing is graded and nothing is scheduled: "No results yet. Your lecturers haven't published anything."
  - `EnrolmentWindowCard` (state per semester from `enrolmentWindows`, `CreditBudget` "15 of 60 credits, Autumn
    {year}", CTA "Browse modules").
  - `NextClassesCard` (today's remaining slots else the next day with classes, "{code} {title} · {kind} · {room}";
    link "Full timetable"; caption "{Semester} timetable").
  - `AnnouncementsCard` (latest 3, link to all).
  - "This year's modules" (`modules`: code, title, credits, semester, link to detail); empty: "You're not enrolled on
    any modules yet." + "Browse modules".
  - `CompletedModulesCard` ("Completed modules", from `completed`, grouped by academic year: code, title, credits,
    "Mark {n} ({band})", "Absent" or "Deferred" when visible, else "Result not yet published").
- **Results release** (`hooks/useResultsRelease`, used by `ResultsSummaryCard` and the scheduled rows of
  `/student/results`): `useCountdown(publishAt)` computes the remaining time against the server clock
  (offset `Date.parse(status.serverTime) - Date.now()` captured when `['public','status']` was fetched). When the
  instant passes while the page is mounted, the hook waits a random 0–30 s (a code comment says why: 20,000 open
  dashboards must not refetch in the same second; 30 s spreads them to about 670 requests per second), shows a
  `Skeleton` over the card with "Results are being released…", then invalidates `['student','dashboard']`,
  `['student','results']` and `['public','status']` exactly once; on success it replaces the countdown with "Your
  results are in" (`role="status"`), toasts "Your results are in.", and moves focus to the card heading.
- Data: `['student','dashboard']` alone (one request) plus `['public','status']` for the server clock.

### `/student/results`

`ResultsTable` grouped by (academic year, semester), newest year first, each group a `<table>` with `<caption>`
"Autumn 2025/26": code (mono), title, credits, mark (tabular) or "Absent" / "Deferred" (`MarkBand` shows an icon and
the word), `MarkBand` text ("First", "2:1", "2:2", "Third", "Fail"; icon for Fail, which also uses the danger token),
published date, and an "Amended {date}" badge when `correctedAt` is set. Scheduled groups show "Publishes {instant}"
with `useResultsRelease`; pending groups show "Results not yet published". Summary row: average so far, indicative
band, `<details>` "How this is calculated" matching `Classification.cs` with the exam-board sentence. Footnote under
the table: `institution.resultsFootnote`. "Print results summary" (print stylesheet renders a header "Unofficial
results summary, not a transcript" and the print date). Empty: "No results yet." plus the publication line when one
is scheduled. Data: `['student','results']`.

### `/student/timetable`

Caption "{Semester} {academicYear} timetable" from `currentSemester`. ≥ 768 px `TimetableGrid` rendered as a
`<table>` (day column headers `<th scope="col">`, time row headers `<th scope="row">`, `aria-labelledby` the page
heading; blocks positioned inside cells with "{code} {title}", kind, room and time, today's column highlighted, "now"
line in hours, overlaps split the cell); < 768 px `TimetableAgenda` (day tabs, default today), which is also rendered
visually hidden at ≥ 768 px as the screen-reader list. "Add to calendar (.ics)" builds weekly VEVENTs client-side
(`features/student/ics.ts`: `SUMMARY:{code} {title} ({kind})`, `LOCATION:{room}`, `RRULE:FREQ=WEEKLY;COUNT=12`, first
occurrence the next matching weekday) and downloads via `lib/download.ts`. Empty: "No classes this semester. Enrol on
modules to build your timetable." Data: `['student','timetable']`.

### `/student/modules` (catalogue)

Filter row (URL-synced): `SearchInput` (code or title, debounced 250 ms), semester (All/Autumn/Spring), level (All/1/2/3),
department (All/CS/MA/PH/EE, derived client-side from the code in `lib/moduleCode.ts` and also present on the
`ModuleSummary`), availability (All/Places available/Full), "Enrolled only". Result count with `aria-live="polite"`;
caption "Places update every 30 seconds. Open a module for live numbers." A banner per semester whose window is
closed or not yet open ("Enrolment for {semester} closed on {closesAt}." / "… opens {opensAt}.") from
`enrolmentWindows`. `CreditBudget` bars for both semesters of the current year. Grid of `ModuleCard`s (2 columns ≥
768, 3 ≥ 1280): code + title, credits and semester, leader, `CapacityMeter` (fill = enrolledCount/capacity, text "12
of 30 places left" always present), `EnrolButton`. Empty: "No modules match these filters" + "Clear filters".

`EnrolButton` states, derived from the module and the student's row in `['student','enrolments']` (the row of any
year for that module):

| Situation | Button | Caption |
|---|---|---|
| active row this year, `canWithdraw` | secondary "Enrolled" (non-interactive) + ghost "Withdraw" | "Withdrawal deadline {withdrawalDeadlineAt}" |
| active row this year, `withdrawBlockedReason = 'results'` | "Enrolled" | "Marks recorded; to withdraw, {support}." |
| active row this year, `withdrawBlockedReason = 'deadline'` | "Enrolled" | "Withdrawal deadline passed {withdrawalDeadlineAt}." |
| active row of an earlier year (`withdrawBlockedReason = 'year'`) | badge "Completed {academicYear}" | "Mark {n} ({band})" when visible in `['student','dashboard'].completed`, else "Result not yet published" |
| withdrawn row, window open, places | primary "Enrol again" | "Withdrawn {withdrawnAt}" |
| module inactive | disabled "Not running" | "{code} is no longer running." |
| full | disabled "Full" | "Full. Places free up when students withdraw; there is no waiting list yet." |
| window `notYetOpen` | disabled "Not open yet" | "Enrolment for {semester} opens {opensAt}." |
| window `closed` | disabled "Enrolment closed" | "Enrolment for {semester} closed on {closesAt}." |
| `noWindow` | disabled "Enrolment closed" | "Enrolment dates for {semester} have not been announced yet." |
| would exceed 60 credits | disabled "Over credit limit" when `['student','enrolments']` is fresh (< 15 s), else enabled | "That would take you over 60 credits for {semester}." (the server's 422 remains the authority) |
| otherwise | primary "Enrol" | "{placesRemaining} places left" |
| pending / 409 full / busy | section 6.3 | section 6.3 |

CS3099 gets no special treatment in code. Data: `['modules','catalogue']`, `['student','enrolments']`.

### `/student/modules/:code`

Header (code, title, badges for credits, semester, level, lecturers), large `CapacityMeter` (the detail refetches
every 10 s while enrolment for its semester is open and the page is visible), description, timetable slots, "Your
status" panel: "Enrolled since {enrolledAt}" / "Completed {academicYear}: mark {n} ({band})" (or Absent / Deferred) /
"You withdrew on {withdrawnAt}." plus "You can re-enrol while places remain." when the window is open / "Not
enrolled", then the same `EnrolButton`. An inactive module shows a banner "This module is no longer running" and a
disabled button. 404 → `EmptyState`. Data: `['modules', code]`, `['student','enrolments']`, `['student','dashboard']`.

### `/lecturer`

Stat tiles (modules taught, students this year, marks entered, marks missing); `GradingProgress` rows per module with
a segmented meter (published / scheduled / submitted / draft / missing as ordinal accent steps plus grey, legend and
numbers) and CTA "Enter marks"; announcements card. Data: `['lecturer','modules']`, `['announcements']`.

### `/lecturer/modules`

Table: code, title, semester, enrolled/capacity, marks status chip (`MarksStatusChip`: No students / Draft / Submitted /
"Scheduled for {instant}" / Published), entered/missing, my role, actions (Roster, Marks, Announcements).
Client-side sort. Empty: "No modules are assigned to you. Ask an administrator."

### `/lecturer/modules/:code` (tabs Roster | Marks | Announcements)

- **Roster**: server-paginated (50/page) `RosterTable` with search (number prefix or any part of the name); columns
  number (mono), name, programme, year, enrolled on, status chip (Active / Withdrawn). "Export CSV" (Should). 403 →
  `ForbiddenState` "This module isn't assigned to you." Empty: "No students are enrolled on {code} this year."
- **Marks** (`/marks`, `?page=&q=`): top bar with status chip, counts from `summary` (entered / missing / total), "Save
  marks" (disabled until dirty; "{n} unsaved" across all pages), "Submit module". Header note "Rows are in
  student-number order, active students first." Paged 100 per page with search; dirty rows persist across page
  changes in component state. Empty: "No students are enrolled on {code} this year."
  - `MarksGrid`: rows for active enrolments first, then withdrawn (greyed, no input, "Withdrawn: not submitted"); per
    row number, name, an outcome `Select` (Mark / Absent / Deferred, default Mark) and a mark `<input type="text"
    inputMode="numeric" pattern="[0-9]*" maxLength={3}>` validated by zod as an integer 0..100 (cleared and disabled
    when the outcome is Absent or Deferred), updated at, entered by, per-row error. Inputs are locked with a lock icon
    when status is not `draft`/`noStudents`. Keyboard: Enter or ArrowDown to the next input, ArrowUp previous; pasting a
    column of numbers fills downward from the focused row within the page; dirty rows get a left accent border;
    `beforeunload` while dirty; `useDirtyForm` registers the grid for `ReauthDialog`.
  - Save sends only dirty rows with their `version`, in chunks of at most 500 rows as sequential `PUT`s; each chunk is
    all-or-nothing; a failed chunk leaves its rows dirty with the error shown and the toast "Saved {a} of {b} changes;
    {c} need attention."; `stale-mark` rows stay dirty and highlighted with the server's value shown.
  - `useMarksMirror`: every change mirrors dirty rows to `sessionStorage['rushday.marks.' + code]` (try/catch; cleared
    on a successful save); on mount a mirror is restored with the banner "Restored {n} unsaved marks from before you
    were signed out." and Save enabled.
  - `SubmitDialog` (`AlertDialog`), shown to the leader only: "Submit {n} marks for {code}? Marks are locked for
    editing and go to the academic office for publication." It lists missing students and disables submit when any are
    missing, with the sentence "Students without a mark can be recorded as Absent or Deferred from the outcome menu; the
    academic office will follow up." A teacher sees no Submit button but the caption "Ask {leader} to submit." With
    no active enrolments Submit is disabled with "No students enrolled".
  - After submit: banner "Submitted on {date}. Marks are locked. Spotted an error? Ask the academic office to return
    this module to draft." Scheduled: "Scheduled for publication on {instant}. Students can't see these marks yet."
    Published: "Published on {instant}."
- **Announcements**: list with future/expired badges; New / Edit (`AnnouncementDialog`: title ≤ 120, body ≤ 4,000 with
  live count, pinned, publish at, expires at) / Delete (`AlertDialog`).
- Data: `['lecturer','module', code, ...]`, `['lecturer','modules']` for the header.

### `/admin` (overview)

Stat tiles (students, lecturers, modules, active enrolments this year, accounts, locked), window chips per semester,
"Results: {submitted} of {modulesTotal} modules submitted for {semester}" with CTA "Publish results", database status,
quick actions (Provision account, Create student, Publish results, New announcement), recent activity (10 `AuditRow`s).
Data: `['admin','overview']`.

### `/admin/students`, `/admin/students/:studentNumber`

Search (number prefix or any part of the name), paged table (25/page): number, name, programme, year, account state
chip, "Left" badge, action "View". Empty: "No students match "{q}"." "Create student" → `CreateStudentDialog` (number,
name, programme, year, email; checkbox "Provision an account now", on by default, which chains to
`POST /api/admin/accounts` and `TemporaryPasswordDialog`).

`StudentSupportPage`: banner "Viewing as administrator. This view is recorded in the audit log."; the record card with
"Edit" (`EditStudentDialog`: name, programme, year, email) and "Mark as left" (`MarkStudentLeftDialog`, an
`AlertDialog` with reason ≥ 10 characters: "Withdraws {n} enrolments this year and disables the account."); account
card (state, last login, actions as on `/admin/accounts`); "Export data" (`GET
/api/admin/students/{n}/export.json`, "Export data (JSON), downloads a file"); enrolments table (year, status, source);
grades table **with status badges**, `visibleToStudent`, "Amended" badge, and the row action "Correct mark" on
Submitted and Published rows (`CorrectMarkDialog`: outcome, mark, reason ≥ 10 characters, warning "The student sees
the corrected mark immediately." when the grade is live); average as the student sees it; override actions
(`OverrideEnrolDialog`: module code combobox, reason ≥ 10 characters, "Raise capacity by one if full" checkbox, and the
warning "Marks for {code} are already submitted. The lecturer will need the module returned to draft to enter a mark
for this student." when the target module is submitted, scheduled or published; `OverrideWithdrawDialog`: reason);
recent audit. Data: `['admin','students', params]`, `['admin','student', n]`.

### `/admin/modules`, `/admin/modules/:code`

Table (client-side filter and sort, 121 rows): code, title, semester, credits, capacity, enrolled this year, leader,
marks status, active. "New module" and Edit → `ModuleEditDialog` (title, description, credits with the warning
"Changing credits affects {n} enrolled students' budgets", capacity with the warning "Below current enrolment ({n})"
that the server rejects, semester, disabled with the caption "Cannot change while {n} students are enrolled" when
`enrolledCount > 0`, active).

`ModuleAdminPage` tabs **Details | Roster | Marks** (`/admin/modules/:code`, `/roster`, `/marks`):
- Details: details, `LecturerAssignmentEditor` (rows staffNumber combobox from `['admin','lecturers', q]`, lecturers who
  have left excluded, + role; exactly one leader enforced client-side and by the server), timetable slots (read-only),
  marks status, and when `enrolledCount > capacity` a warning with "Trim to capacity" (`TrimToCapacityDialog`, an
  `AlertDialog`: "Withdraws the {n} most recent enrolments so {code} has {capacity} students. Each is recorded in the
  audit log." with reason).
- Roster: `ModuleRosterTable` from `GET /api/admin/modules/{code}/roster` (same columns as the lecturer roster).
- Marks: `ModuleMarksTable` from `GET /api/admin/modules/{code}/marks`, read-only (no inputs, no Save or Submit), with
  "Correct" on Submitted / Scheduled / Published rows opening `CorrectMarkDialog`.
Data: `['admin','modules', includeInactive]`, `['admin','module', code, ...]`.

### `/admin/lecturers`

Table: staff number, name, title, department, modules, account state, "Left" badge; row actions "Edit"
(`EditLecturerDialog`) and "Mark as left" (`MarkLecturerLeftDialog`, reason, "Disables the account; their module
assignments stay and show as left."); "Create lecturer" → `CreateLecturerDialog`.

### `/admin/accounts`

Filters (search, role, state; URL-synced), paged table (25/page): username (mono), display name, role badge, linked
number, state chips ("Locked until {lockoutEnd}" for an Identity lockout, "Locked by administrator" when `lockoutEnd`
is 9999-12-31, Disabled, Must change password, Two-step on, Demo), last sign-in, `AccountRowActions` (Reset password,
Lock/Unlock, Disable/Enable, Reset two-step verification (`ResetMfaDialog`), View student). Tooltips: "Lock: a
temporary block that keeps the account (for example after suspicious activity)"; "Disable: the person has left;
sign-in stops within 5 minutes". Actions on `isDemo` rows are disabled with the tooltip "Demo accounts are read-only".
Resetting your own password warns "You'll be signed out after copying the temporary password." Empty: "No accounts
match these filters."

"Provision account" → `ProvisionAccountDialog` (role first; Student → student number `Combobox` from
`GET /api/admin/students?accountState=none`, with the empty state "Every student already has an account. Create a
student first, or use Reset password on an existing one." and a button opening `CreateStudentDialog` that returns with
the new number selected; Lecturer → staff number combobox from `['admin','lecturers', q]` filtered to
`hasAccount = false`; Admin → none; optional temporary password) → `TemporaryPasswordDialog` shows the password once
with Copy and "This won't be shown again. The person must change it and, for administrators, set up two-step
verification at first sign-in." Demo banner when `status.demo` is not null: "Demo mode is on: {n} demo accounts use
published passwords. They are disabled automatically on the first start with Demo__Enabled off."

### `/admin/enrolment` (windows)

One `WindowEditor` card per window (academic year, semester, opens, closes, withdrawal deadline as `datetime-local` in
the institution zone with the zone named), state chip, Save, Delete (`AlertDialog`); "Add window"; shortcuts "Open now
for 7 days" and "Close now" (`AlertDialog` "Students who are enrolling right now will see Enrolment closed.
Continue?"). Client validation mirrors `window-dates-invalid`.

### `/admin/results`

Year and semester pickers (year defaults to settings). `SubmissionProgressTable`: code, title, leader, enrolled,
entered/missing, `MarksStatusChip`, and the inline action for each state: draft "Waiting for the lecturer"; submitted
with `missing = 0` "Ready to publish"; submitted with `missing > 0` "Submitted, {n} marks missing (a student without
a submitted mark): Return to draft"; scheduled "In the publication scheduled for {instant}: Return to draft";
published "Live: correct single marks from the Marks tab". Sorted by state then code; toggle "Hide modules without
students", on by default. Empty: "Nothing submitted for {semester} {year} yet; lecturers submit from their Marks page."

`ReturnToDraftDialog` (`AlertDialog`, reason ≥ 10 characters): for a submitted module "The lecturers can edit marks
again and must resubmit."; for a scheduled one "Students have not seen these marks. {code} will be removed from the
publication scheduled for {publishAt}."

`PublishDialog` (`AlertDialog`): "Publish now" toggle or a date-time picker (min now, max +90 days) in the institution
zone; preview "Will publish {n} modules ({grades} marks); {m} modules excluded" with the excluded list and reasons
("not submitted" / "{k} marks missing"); checkbox "Post a pinned university announcement: {Semester} {year} results
are available" (checked); required checkbox "The exam board has approved these marks" that enables the primary button;
the sentence "This is the results-day button. Students see these marks at the chosen instant."; primary label "Publish
{n} modules now" or "Schedule {n} modules for {instant} ({zone})". Success toast "Published: {n} modules, {grades}
marks. Students see them {now | at {instant}}." with a link to the history row.

Publication history table with state chips; scheduled rows: "Reschedule" (`RescheduleDialog`) and "Cancel"
(`CancelPublicationDialog`, `AlertDialog`: "Marks go back to Submitted and no student will see them."); live rows:
"Unpublish" (`UnpublishDialog`, `AlertDialog` with reason: "Students stop seeing these {grades} marks immediately.").
Data: `['admin','results', academicYear, semester]`.

### `/admin/announcements`

List with scope badge, pinned, published/expires, author; New (university scope) / Edit / Delete.

### `/admin/audit`

`AuditFilters` (actor, action select from the catalogue, student number, module code, date range presets Today / 7 / 30
days / custom; URL-synced). Table (50/page): time (absolute; relative in tooltip), actor + role, action label, subject,
student, module, expand control showing `details` as a definition list (text only). "Export CSV (up to 50,000 rows),
downloads a file" uses the same filters; when the response carries `X-RushDay-Truncated: true` a toast says "The
export stopped at {cap} rows. Narrow the date range to 31 days or less to get everything." Empty: "Nothing recorded
for these filters."

### `/admin/ops`

Written for a registry manager first and an engineer second: every tile has a plain primary label, the metric name as
a `font-mono` caption, and a tooltip `description`.

- `HealthSummary` (first row, `role="status"`): "Running normally" / "Busy: some visitors are being asked to try
  again" (any `shed503` or `rateLimited429` in the last minute) / "Struggling: database waits or server errors in the
  last minute" (`status5xx > 0` or a `waitTimeoutsTotal` increase since the previous sample), each with one sentence,
  followed by "Last minute: {requests} requests, {successPct}% succeeded, typical response {p50} ms, slowest 5% under
  {p95} ms, {shed + rateLimited} asked to try again, database {ok|degraded}."
- Header: commit, environment, "Running since {startedAt} ({uptime})", "Sampled {n}s ago" (`aria-live="polite"`,
  one update per sample).
- `LiveTiles` (60-sample client-side sparklines, `aria-hidden`, with the current value and the 60-sample min/max in the
  tile text): "Visits per second" (`requests/s`), "Pages in progress" (`in flight`), "Slowest 5% of requests" (`p95
  ms`), "Slowest 1 in 100" (`p99 ms`), "Server errors" (`5xx`), "Asked to slow down" (`429`), "Turned away while busy
  (fast try-again)" (`503 shed`), "Memory in use" (`working set MB`).
- `PoolMeter`: "Database connections in use: {busy} of {max}", pending requests, and "Times a page gave up waiting for
  the database since the server started: {n}" (`db.client.connections.timeouts`, what broke the first version).
- `RequestRateChart` and `LatencyChart` (last 60 minutes from `series`) under the line "History starts when the server
  last started ({startedAt})"; empty series: "Collecting the first minute of data…".
- `CountersPanel`: "Enrolment requests: accepted / full / already enrolled / closed / over credit limit / completed
  already / other", "Answered from memory: {pct}%" per cache, "Database round trips per dashboard: {n} (was 15)",
  sign-ins succeeded / failed / lockouts; totals labelled "since the server started {uptime} ago".
- `DataQualityPanel`: "Data checks: modules with more students than places" listing `modulesOverCapacity` with each
  code linking to `/admin/modules/{code}` (where "Trim to capacity" lives), and "Re-count places" (`POST
  /api/admin/ops/reconcile`); when `dataQuality.staleSince` is set, "Checks last ran {n} min ago". When `status.demo` is
  not null, a "Reset demo module" button (`POST /api/admin/ops/demo-reset`, `AlertDialog` "Withdraws every self-service
  CS3099 enrolment that has no mark, so demo visitors can enrol again.").
- `TechnicalDetails` (collapsed `<details>`): GC heap, thread-pool threads, `runtime.*` (GC mode, pool size,
  rate-limit settings), `BackfillsTable` (human label per step with the key as a mono caption, for example "Freed
  CS3099's places for the demo" / `demo_reset_hot_module`), `enrolledCountDrift`.
- "The load story": the same chart cards as `/story` with Chart/Table toggles.
- Closing note: "This page updates while it is open. RushDay does not send alerts in this edition; check it after a
  deploy and on results day."
- When a poll fails, the page keeps the last snapshot, greys the tiles and shows "Last sample {n}s ago; the server is
  too busy to answer right now" instead of an `ErrorState`.
Data: `['admin','ops','metrics']` (5 s, paused when hidden), `['load-results']`, `['public','status']`.

### `/admin/settings`

Form: academic year (with the warning "Changing the year starts a new year for everyone: this year's enrolment
windows no longer apply, credit budgets and module places start from zero, and last year's modules become
'completed'. Create the windows for {new year} first."), current semester (Autumn / Spring, "Decides whose classes
appear on this week's timetables"), institution name and short name, time zone (IANA id with an example), academic
office email and help URL ("Shown wherever students are told to contact the academic office"). Save → `PUT
/api/admin/settings`; changing the year asks for confirmation in an `AlertDialog`.

## 11. Charts (`features/ops/charts`)

Method: choose the form, assign colour by job (emphasis scheme: accent for "after", grey for "before", status red only
for genuine errors), validate against RushDay's surfaces, then marks, hover and a table twin. Palette validation:
`#2f5bea` on `#ffffff` and `#3987e5` on `#171b21` pass; `#8aa4ff` fails the dark lightness band and is never a chart
mark; `#898781` is the de-emphasis grey; red/green status pairs fail deutan separation, so status is always icon +
label. Series are labelled "Before (v0)" and "After (v1)" in legends and end labels, never bare "v0"/"v1".

| Chart | Form | Encoding |
|---|---|---|
| `EnrolmentRushChart` | Hero figure "Places given out beyond capacity: 124 → 0" above two horizontal 100%-stacked bars (Before (v0), After (v1)) of 500 requests: "Got a place" (accent), "Told it was full" (grey, the correct outcome), "Turned away while busy" (accent-2), "Failed" (critical). 2 px surface gaps; legend; values in tooltip and table. | part-to-whole, emphasis + status |
| `DashboardKneeCharts` | Three small multiples sharing the x-axis (target load 1,000–4,000 requests per second): "Slowest 5% of requests (ms)", "Requests actually served per second", "Requests the test could not even send, or that were turned away". Two series each: Before (v0) (grey, 2 px line, 8 px markers with surface ring) and After (v1) (accent). One y-axis per chart; end labels; legend. | change across load |
| `ResultsDayTiles` | KPI row: peak load 800 requests/s, slowest 5%, slowest 1 in 100, failed %, "Database round trips per page: 15 → 5", each showing Before and After with the delta. | headline numbers |
| `LoginStormTiles` | KPI row: sign-ins per second reached, accepted vs "asked to wait" counts, slowest 5%; spray mode: "attacker slowed after 20 wrong passwords; the real student still signed in". | headline numbers |
| `RequestRateChart` (live) | Single-series line of requests per second over the last 60 minutes; area wash 10%; no legend. | trend |
| `LatencyChart` (live) | typical (p50) / slowest 5% (p95) / slowest 1 in 100 (p99) as ordinal steps (`--chart-accent-2`, `--chart-accent`, `--chart-accent-3`), legend + end labels. | ordinal |
| `PoolMeter` | Meter with same-hue lighter track; warning at 80%, danger at 95% with a text label. | ratio |

Shared rules: bars ≤ 24 px with 4 px rounded data-ends; hairline gridlines in `--chart-grid`; axis and label text in
text tokens; tooltips list every series at the hovered x; containers include the axis band (no nested scroll);
`ChartCard` wraps each chart with a title, "What this means" (one plain sentence, for example "Before, 154 students got
30 places. After, exactly 30 did and the rest were told immediately."), "Decision record" (the ADR link), "Raw test
output" (the results file) and `Tabs` Chart / Table where `ChartTable` renders the identical numbers;
`<figure aria-labelledby>` with a visually hidden summary sentence; `isAnimationActive={false}` under
`prefers-reduced-motion`. Recharts is only in the `charts` chunk, loaded by `/story` and `/admin/ops`.

## 12. Accessibility (WCAG 2.2 AA)

- Structure: `SkipLink` to `#main`; landmarks `header`, `nav aria-label="Primary"`, `main`, `footer`; one `h1` per page;
  document title per route; focus to the `<h1 tabIndex={-1}>` on route change; `lang="en-GB"`.
- Keyboard: every action reachable; visible focus ring (2 px, offset 2 px); dialogs trap and restore focus; Radix
  arrow-key patterns for menus and tabs; marks grid Enter/Arrow navigation with `scroll-padding-bottom` under the sticky
  bar; Escape closes overlays; no traps.
- Forms: programmatic labels, `autocomplete` tokens (`username`, `current-password`, `new-password`, `one-time-code`),
  errors announced and linked, required in text; numeric inputs are `type="text" inputMode="numeric"` (no spinners, no
  wheel changes).
- Colour: never colour alone; contrast per section 9.1 in both themes; legends for every multi-series chart.
- Motion: `prefers-reduced-motion` disables sparkline animation and transitions.
- Live regions: sonner toasts are `role="status"` for success and info and `role="alert"` for errors (`toast.error`);
  result counts `aria-live="polite"`; the ops sample timestamp at most once per sample; countdowns per section 9.3
  (digits `aria-hidden`, one status sentence per minute); loading regions `aria-busy`.
- Non-visual equivalents: chart table twins; sparkline values in tile text; the timetable as a `<table>` with row and
  column headers plus the visually hidden agenda list.
- Targets ≥ 44 × 44 on coarse pointers; ≥ 24 × 24 elsewhere. Usable at 200% zoom and 320 px width.
- Dates: absolute always shown; zone named where ambiguous (windows, publication instants).
- Downloads announced in the label ("Export CSV, downloads a file").
- Verification: axe (`wcag2a, wcag2aa, wcag22aa`) in Playwright on every page in both themes; zero `serious`/`critical`.

## 13. Tests

### 13.1 Vitest + Testing Library + MSW (`src/**/*.test.tsx`, `src/test/`)

`renderWithProviders(ui, { route, user, handlers })` wraps a fresh `QueryClient` (retries off), `MemoryRouter` and an
`AuthProvider` primed with a `Me` fixture (and `configureClient` fakes); `src/test/server.ts` (S5) creates the MSW
`server` with **only** the auth and public handlers; feature tests pass their own handlers through
`renderWithProviders(ui, { handlers })`, which calls `server.use(...handlers)` and `server.resetHandlers()` after each
test, so stages S7–S10 never edit `server.ts`. Factories for `DashboardResponse`, `ModuleSummary`, `MarksSheet`,
`AccountView`, `AuditEventView`, `OpsSnapshot`.

Required tests:
- S5: `api/client.test.ts` (CSRF header on non-GET only; `ApiError.kind` and `retryAfterSeconds`; `onUnauthenticated`;
  antiforgery refresh and single retry; blob with headers), `api/problem.test.ts` (every slug of the closed catalogue
  maps; unknown 4xx uses `detail`; unknown 5xx includes `traceId`; the three `enrolment-window-closed` branches),
  `api/queryClient.test.ts` (no retry on 4xx including 429; `retryDelay` honours `Retry-After` plus jitter),
  `app/guards.test.tsx` (anonymous → `/login?returnTo=`; wrong role → `/forbidden`; must-change → password page;
  MFA gate after the password gate; `returnTo` sanitiser rejects `//evil`, `/\evil.com`, `https://evil.example` and
  `/login`), `app/AuthProvider.test.tsx` (boot 200/401; cold-start notice at 3 s with fake timers; logout clears cache;
  `mfaPending` → verify; a 401 while a form is dirty opens `ReauthDialog` and a successful re-login lets the save go
  through), `features/auth/LoginPage.test.tsx` (validation; demo "Use"; 401/429 copy; lockout hint from the third 401;
  status 503 still renders a usable form; story blockquote above the form in demo mode and below it otherwise;
  redirect), `ChangePasswordPage` (checklist; server errors; required mode hides nav; demo users see no form),
  `MfaSetupPage` (QR rendered from a mocked `qrcode`; enable success and wrong code), `lib/useCountdown.test.ts`
  (server offset; fake timers: passing `publishAt` fires `onElapsed` exactly once after a delay between 0 and 30 s),
  `lib/contrast.test.ts`, `lib/returnTo.test.ts`, `components/ui/*` (Button loading; FormField wiring; Table stacked
  mode and `aria-sort` on `th`; ThemeToggle with a throwing `localStorage`; Countdown live-region throttling).
- S7: `student/CataloguePage` (filters and URL sync; every `EnrolButton` state of the table; empty state; live count),
  `student/hooks/useEnrol` (pending shows "Enrolling…"; 201 updates catalogue and module caches and toasts; 409
  `module-full` shows "Filled while you were enrolling"; 503 disables the button for `Retry-After` with a countdown;
  no automatic retry), `useWithdraw` (optimistic; rollback; dialog copy for open and closed windows),
  `student/ResultsPage` (grouping by year and semester; bands for 70/60/50/40/39; Absent/Deferred rows; "Amended"
  badge; weighted average 80×30 + 50×15 → 70.0 and absences excluded; scheduled copy),
  `student/ResultsSummaryCard` ("countdown reaching zero triggers one invalidation within 30 s", fake timers),
  `student/TimetableGrid` (table semantics) and `ics.test.ts` (SUMMARY, COUNT=12).
- S8: `lecturer/MarksGrid` (0–100 validation; outcome select clears and disables the mark; dirty count across pages;
  Enter moves focus; locked rows; `stale-mark` rows stay dirty; 1,300 rows paged, 600 dirty rows saved in two
  requests; the sessionStorage mirror restores after a remount), `SubmitDialog` (lists missing; hidden for teachers
  with the caption; disabled with no students).
- S9: `admin/AccountsPage` (paging keeps data; provision → temporary password shown once; empty student combobox state;
  demo rows' actions disabled; lock chip copy), `admin/ResultsAdminPage` (publish preview and excluded reasons; exam
  board checkbox gates the button; max +90 days; zone rendering; cancel, unpublish and return-to-draft dialogs for the
  scheduled case), `admin/CorrectMarkDialog`, `admin/AuditLogPage` (filters → query params; details as text: a
  `<script>` string renders as text; truncation toast), `admin/SettingsPage` (year-change warning and confirmation).
- S10: `ops/HealthSummary` (the three states), `ops/OpsPage` (keeps the last snapshot on a failed poll; empty series
  copy), `ops/charts/*` (table twin equals chart data; "not yet measured"; "Before (v0)"/"After (v1)" labels).

### 13.2 Playwright (`e2e/`, `playwright.config.ts`)

```ts
export default defineConfig({
  testDir: './e2e', fullyParallel: false, workers: 1, forbidOnly: !!process.env.CI, retries: process.env.CI ? 1 : 0,
  reporter: [['list'], ['html', { open: 'never' }]],
  use: { baseURL: process.env.E2E_BASE_URL ?? 'http://localhost:5080', trace: 'retain-on-failure' },
  projects: [{ name: 'desktop-chromium', use: { ...devices['Desktop Chrome'] } },
             { name: 'mobile-chromium', use: { ...devices['Pixel 7'], viewport: { width: 360, height: 780 } } }],
  webServer: process.env.E2E_BASE_URL ? undefined : {
    // Smart App Control blocks `dotnet run` on the owner's machine, so locally the API runs from the single-file publish
    // that scripts/e2e.ps1 has just produced; CI sets E2E_SERVER_COMMAND to `dotnet run ... --no-build`.
    command: process.env.E2E_SERVER_COMMAND
      ?? 'powershell -NoProfile -ExecutionPolicy Bypass -File ../../scripts/run-api.ps1 -NoPublish',
    url: 'http://localhost:5080/api/health/live', reuseExistingServer: !process.env.CI, timeout: 180_000,
    env: { ASPNETCORE_ENVIRONMENT: 'Development', ASPNETCORE_URLS: 'http://localhost:5080',
           ConnectionStrings__RushDay: process.env.E2E_CONNECTION_STRING ?? 'Host=localhost;Port=5432;Database=rushday_e2e;Username=rushday;Password=rushday',
           Database__MigrateOnStartup: 'true', Database__SeedOnStartup: 'true', Database__BackfillOnStartup: 'true',
           Database__SeedStudentCount: '300', Database__SeedResultsDay: '2026-01-26T09:00:00Z', Demo__Enabled: 'true',
           Auth__SecurityStampIntervalMinutes: '0' } },
})
```

`e2e/fixtures.ts`: `loginAs(page, 'student' | 'lecturer' | 'admin' | { username, password })` reads
`GET /api/public/status` for demo credentials, calls `GET /api/auth/csrf` and `POST /api/auth/login` through
`page.request` (cookies land in the browser context whatever their names), then `page.goto('/')`; `api(page)` helper
performs authenticated API calls with the CSRF token for setup steps; `e2e/totp.ts` computes RFC 6238 codes from a
base32 `sharedKey` (Node `crypto`, SHA-1, 30 s, 6 digits). One journey signs in through the real form. The demo
administrator is exempt from the second factor while demo mode is on, so `loginAs('admin')` needs no code.

Specs that change shared state irreversibly (`lecturer-marks`, `admin-accounts`, `admin-mfa`) run on
`desktop-chromium` only (`test.skip(testInfo.project.name !== 'desktop-chromium')`); the others run on both projects
and restore what they change.

Journeys: `login.spec.ts` (form sign-in; wrong password copy and the lockout hint after three attempts; `/student`
unauthenticated → `/login?returnTo=%2Fstudent` → lands on `/student`; sign-out; authenticated `/login` redirects home),
`student-results-day.spec.ts` (dashboard "Average so far" and band chip; completed modules listed under 2025/26;
results grouped "Autumn 2025/26"; timetable shows the current semester; ICS download begins with `BEGIN:VCALENDAR`),
`student-enrol.spec.ts` (S000010 filters for CS3099, enrols: "Enrolling…" then "Enrolled" and the toast; detail shows
enrolled; withdraws through the dialog; count restored; after admin sets CS3099 capacity to `enrolledCount` a further
enrol shows "Full"; `afterAll` restores capacity 30 through `api(page)`), `lecturer-marks.spec.ts` (admin
override-enrols two students on CS3099 via API with `forceCapacity: true`; L00001 enters one mark and one Absent,
saves, submits with the confirmation; inputs lock; admin publishes Spring 2026/27 "now" through `PublishDialog` with
the exam-board checkbox; the student signs in and sees the mark; admin corrects it with a reason; the student sees the
new mark with "Amended"), `admin-accounts.spec.ts` (create lecturer `L9` + four random digits with a unique username,
provision the account, copy the temporary password, sign in as them → forced change → `/lecturer`; disable an account
and its open session lands on `/login` on the next navigation; audit rows visible), `admin-mfa.spec.ts` (provision an
administrator with a unique username, sign in → forced password change → forced `/account/mfa` → read the `sharedKey`
from the page, compute a code with `e2e/totp.ts`, enable → sign out → sign in → code step → `/admin`),
`guards.spec.ts` (student on `/admin` → `/forbidden`; lecturer on `/student` → `/forbidden`; unknown route; deep-link
refresh on `/student/results`; `/api/does-not-exist` returns JSON 404), `ops-and-story.spec.ts` (`/story` renders
chart cards, glossary and table twins without login; `/admin/ops` shows the health summary, samples update and the
pool meter renders), `mobile.spec.ts` (no horizontal overflow on login, dashboard, catalogue, marks; bottom tabs; drawer
focus trap), `a11y/pages.spec.ts` (axe on every page in both themes).

`scripts/e2e.ps1` (Windows PowerShell 5.1 compatible): drops and recreates `rushday_e2e` with `psql` (same discovery as
`scripts/reset-db.ps1`), `npm run build` in `src/RushDay.Web`, `scripts/run-api.ps1 -PublishOnly`, then
`npm run test:e2e` in `src/RushDay.Web` (Playwright starts the published exe through the `webServer` command).

## 14. Build and CI integration

- `.gitignore`: already ignores `src/RushDay.Api/wwwroot/`, `node_modules/`, `dist/`, `playwright-report/`,
  `test-results/` and `coverage/`, and `wwwroot` is untracked; S3 adds only `blob-report/`. The interim
  `wwwroot/index.html` exists only in working trees and is deleted from the working tree by S2; nothing is removed from
  git.
- `.dockerignore`: keeps its entries and adds `src/RushDay.Web/e2e/` and `src/RushDay.Web/blob-report/`.
- `Dockerfile` (the existing web and build stages stay; the runtime stage and the pins change):

```dockerfile
FROM node:24-alpine@sha256:<digest> AS web
WORKDIR /src/src/RushDay.Web
COPY src/RushDay.Web/package.json src/RushDay.Web/package-lock.json ./
RUN npm ci --no-audit --no-fund
COPY src/RushDay.Web/ ./
RUN npm run build

FROM mcr.microsoft.com/dotnet/sdk:10.0@sha256:<digest> AS build
WORKDIR /src
COPY global.json Directory.Build.props ./
COPY src/RushDay.Domain/RushDay.Domain.csproj src/RushDay.Domain/
COPY src/RushDay.Infrastructure/RushDay.Infrastructure.csproj src/RushDay.Infrastructure/
COPY src/RushDay.Api/RushDay.Api.csproj src/RushDay.Api/
RUN dotnet restore src/RushDay.Api/RushDay.Api.csproj
COPY src/RushDay.Domain/ src/RushDay.Domain/
COPY src/RushDay.Infrastructure/ src/RushDay.Infrastructure/
COPY src/RushDay.Api/ src/RushDay.Api/
COPY --from=web /src/src/RushDay.Api/wwwroot/ src/RushDay.Api/wwwroot/
RUN dotnet publish src/RushDay.Api/RushDay.Api.csproj --configuration Release --no-restore --output /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled@sha256:<digest> AS runtime
WORKDIR /app
COPY --from=build /app .
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
USER $APP_UID
ENTRYPOINT ["dotnet", "RushDay.Api.dll"]
```

  `<digest>` is resolved by the S3 agent at implementation time (`docker buildx imagetools inspect` in CI, or the
  registry's `Docker-Content-Digest` header for the tag) and kept current by Dependabot's `docker` ecosystem. The
  chiseled runtime has no shell and runs as the non-root `app` user.
- `.github/dependabot.yml` (S3): ecosystems `github-actions` (`/`), `docker` (`/`), `nuget` (`/`), `npm`
  (`/src/RushDay.Web`), weekly; security updates are enabled in the repository settings (`06-implementation-plan.md` S0).
- `scripts/dev.ps1`: runs `scripts/run-api.ps1` (publish then start; re-run it after backend changes, because Smart App
  Control blocks `dotnet watch run`) in one window and `npm run dev` in another, and prints `http://localhost:5173`.
- `.github/workflows/ci.yml` (every action pinned by commit SHA with the version in a comment):

```
web            ubuntu-latest; setup-node (node 24, cache npm, cache-dependency-path src/RushDay.Web/package-lock.json);
               every step with working-directory: src/RushDay.Web:
               npm ci → npm run lint → npm run typecheck → npm run test:coverage → npm run load-results -- --check
               → npm run build → npm run bundle-budget → npm audit --omit=dev --audit-level=high
               → npm audit (continue-on-error: true)
               upload-artifact web-dist = src/RushDay.Api/wwwroot
build-and-test needs: web; download web-dist → src/RushDay.Api/wwwroot; setup-dotnet (global.json); restore; build -warnaserror;
               dotnet list package --vulnerable --include-transitive (fail on findings); unit tests; integration tests (Testcontainers);
               docker build; pwsh scripts/check-story.ps1 (added by S12, once S11 has written README and ADR 0007); upload TestResults
e2e            needs: web; services postgres:18 (rushday/rushday, pg_isready); setup-dotnet; setup-node; download web-dist;
               dotnet build -c Release; npm ci; npx playwright install --with-deps chromium;
               env E2E_SERVER_COMMAND="dotnet run --project ../RushDay.Api --configuration Release --no-build",
                   E2E_CONNECTION_STRING=Host=localhost;Port=5432;Database=rushday;Username=rushday;Password=rushday,
                   Auth__SecurityStampIntervalMinutes=0;
               npx playwright test; upload playwright-report (always)
```

All three jobs are required checks on `main` (branch protection, S0); Render's `autoDeployTrigger: checksPass` waits
for the commit's checks, so a red `e2e` blocks the deploy with no further Render change. Bundle budget: entry +
`react` + `query` chunks ≤ 220 KB gzip; neither the `charts` chunk nor the `qrcode` chunk may be statically imported
from the entry graph.
