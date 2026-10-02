import { defineConfig, devices } from '@playwright/test'

/**
 * End-to-end journeys and accessibility scans (05-frontend.md section 13.2). One worker, in file
 * order, against one API and one database: the specs share the seeded demo data, so they run one at
 * a time and each restores what it changes (the three that cannot run on desktop-chromium only).
 *
 * Without E2E_BASE_URL Playwright starts the API itself. Smart App Control blocks `dotnet run` on the
 * owner's machine, so locally that is the single-file publish scripts/e2e.ps1 has just produced
 * (through scripts/run-api.ps1 -NoPublish); CI sets E2E_SERVER_COMMAND to `dotnet run ... --no-build`.
 * E2E_PORT (default 5080) moves the whole run to another port, so it can sit beside a dev API.
 */
const port = process.env.E2E_PORT ?? '5080'
const origin = `http://localhost:${port}`

export default defineConfig({
  testDir: './e2e',
  fullyParallel: false,
  workers: 1,
  forbidOnly: !!process.env.CI,
  retries: process.env.CI ? 1 : 0,
  reporter: [['list'], ['html', { open: 'never' }]],
  // Prepares the non-demo administrator (password change and authenticator) once the API is up.
  globalSetup: './e2e/global-setup.ts',
  use: { baseURL: process.env.E2E_BASE_URL ?? origin, trace: 'retain-on-failure' },
  projects: [
    { name: 'desktop-chromium', use: { ...devices['Desktop Chrome'] } },
    {
      name: 'mobile-chromium',
      use: { ...devices['Pixel 7'], viewport: { width: 360, height: 780 } },
    },
  ],
  webServer: process.env.E2E_BASE_URL
    ? undefined
    : {
        command:
          process.env.E2E_SERVER_COMMAND ??
          'powershell -NoProfile -ExecutionPolicy Bypass -File ../../scripts/run-api.ps1 -NoPublish',
        url: `${origin}/api/health/live`,
        reuseExistingServer: !process.env.CI,
        timeout: 180_000,
        env: {
          ASPNETCORE_ENVIRONMENT: 'Development',
          ASPNETCORE_URLS: origin,
          ConnectionStrings__RushDay:
            process.env.E2E_CONNECTION_STRING ??
            'Host=localhost;Port=5432;Database=rushday_e2e;Username=rushday;Password=rushday',
          Database__MigrateOnStartup: 'true',
          Database__SeedOnStartup: 'true',
          Database__BackfillOnStartup: 'true',
          Database__SeedStudentCount: '300',
          Database__SeedResultsDay: '2026-01-26T09:00:00Z',
          Demo__Enabled: 'true',
          Auth__SecurityStampIntervalMinutes: '0',
        },
      },
})
