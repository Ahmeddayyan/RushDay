import { defineConfig, devices } from '@playwright/test'

// End-to-end tests run against an API that is already up and serving the built SPA
// (dotnet run --project src/RushDay.Api -c Release, after npm run build). There is deliberately
// no webServer block: the same tests can point at a deployed instance via E2E_BASE_URL.
export default defineConfig({
  testDir: './tests/e2e',
  fullyParallel: true,
  forbidOnly: !!process.env.CI,
  retries: process.env.CI ? 2 : 0,
  reporter: [['list'], ['html', { open: 'never' }]],
  use: {
    baseURL: process.env.E2E_BASE_URL ?? 'http://localhost:5080',
    trace: 'on-first-retry',
  },
  projects: [
    {
      name: 'chromium',
      use: { ...devices['Desktop Chrome'] },
    },
  ],
})
