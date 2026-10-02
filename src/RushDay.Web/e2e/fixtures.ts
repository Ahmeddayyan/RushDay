import { readFileSync } from 'node:fs'
import { join } from 'node:path'

import {
  test as base,
  expect,
  type APIRequestContext,
  type APIResponse,
  type Browser,
  type BrowserContext,
  type Page,
  type TestInfo,
} from '@playwright/test'

import { freshTotp } from './totp.ts'

/**
 * Shared helpers for the journeys (05-frontend.md section 13.2): `loginAs` signs in through the real
 * `GET /api/auth/csrf` and `POST /api/auth/login` (so the cookies land in the browser context,
 * whatever their names), `api` performs authenticated setup calls with the CSRF token, `setTheme`
 * pins light or dark, and the `adminApi` fixture is a demo-administrator API session for setup and
 * clean-up. The non-demo "root" administrator that global-setup.ts prepares (the demo administrator
 * is a demo actor and cannot provision real accounts, 02-api.md section 8.5) is reached through
 * `ROOT_ADMIN_STATE`.
 */

export type Role = 'student' | 'lecturer' | 'admin'

export interface Credentials {
  username: string
  password: string
}

export interface Me {
  id: string
  username: string
  displayName: string
  role: 'Student' | 'Lecturer' | 'Admin'
  mustChangePassword: boolean
  mfaEnabled: boolean
  mfaSetupRequired: boolean
  isDemo: boolean
  csrfToken: string
}

interface MfaChallenge {
  mfaRequired: true
  csrfToken: string
}

interface PublicStatus {
  academicYear: string
  demo: { accounts: { role: string; username: string; password: string }[] } | null
}

const ROLE_NAMES: Record<Role, Me['role']> = {
  student: 'Student',
  lecturer: 'Lecturer',
  admin: 'Admin',
}

export const ROLE_HOME: Record<Me['role'], string> = {
  Student: '/student',
  Lecturer: '/lecturer',
  Admin: '/admin',
}

/**
 * The bootstrap administrator that scripts/e2e.ps1 and the CI e2e job create with a start in which
 * demo mode is off (01-domain-and-data.md section 6 step 5), so it is a real account: it must change
 * its password and set up two-step verification, and what it provisions is real too.
 */
export const ROOT_ADMIN = {
  username: process.env.E2E_ADMIN_USERNAME ?? 'e2e-admin',
  bootstrapPassword: process.env.E2E_ADMIN_PASSWORD ?? 'Harbour-Lantern-4821',
  password: 'Quiet-Meadow-Compass-77',
} as const

// Beside the TypeScript build info (tsconfig*.json): ignored by git, ESLint and Prettier alike.
const AUTH_DIR = join(import.meta.dirname, '..', 'node_modules', '.tmp', 'e2e-auth')
/** Playwright storage state (the root administrator's session cookies), written by global-setup.ts. */
export const ROOT_ADMIN_STATE = join(AUTH_DIR, 'root-admin.state.json')
/** The root administrator's current password, authenticator key and last accepted TOTP step. */
export const ROOT_ADMIN_SECRETS = join(AUTH_DIR, 'root-admin.json')

export interface RootAdminSecrets {
  username: string
  password: string
  sharedKey: string
  lastStep: number
}

export function readRootAdminSecrets(): RootAdminSecrets | null {
  try {
    return JSON.parse(readFileSync(ROOT_ADMIN_SECRETS, 'utf8')) as RootAdminSecrets
  } catch {
    return null
  }
}

async function readJson<T>(response: APIResponse, what: string): Promise<T> {
  if (!response.ok()) {
    throw new Error(`${what} answered ${response.status()}: ${await response.text()}`)
  }
  return (await response.json()) as T
}

/** The demo account of a role, as `GET /api/public/status` publishes it in demo mode. */
export async function demoCredentials(
  request: APIRequestContext,
  role: Role,
): Promise<Credentials> {
  const status = await readJson<PublicStatus>(
    await request.get('/api/public/status'),
    'GET /api/public/status',
  )
  const account = status.demo?.accounts.find((item) => item.role === ROLE_NAMES[role])
  if (!account) throw new Error('Demo mode is off: the public status lists no demo accounts.')
  return { username: account.username, password: account.password }
}

/** Another seeded student (S000001–S000300) with the published demo student password. */
export async function demoStudent(
  request: APIRequestContext,
  studentNumber: string,
): Promise<Credentials> {
  const { password } = await demoCredentials(request, 'student')
  return { username: studentNumber, password }
}

export interface SignInOptions {
  /** Answers the second-factor step, when the account has one, with a fresh code of this key. */
  totp?: { sharedKey: string; lastUsedStep?: number; onUsed?: (step: number) => void }
}

/**
 * `GET /api/auth/csrf`, then `POST /api/auth/login` (and `POST /api/auth/mfa/verify` when the account
 * has a second factor) through a request context whose cookie jar is the browser's when it is
 * `page.request`.
 */
export async function signIn(
  request: APIRequestContext,
  who: Role | Credentials,
  options: SignInOptions = {},
): Promise<Me> {
  const credentials = typeof who === 'string' ? await demoCredentials(request, who) : who
  const { csrfToken } = await readJson<{ csrfToken: string }>(
    await request.get('/api/auth/csrf'),
    'GET /api/auth/csrf',
  )
  const answer = await readJson<Me | MfaChallenge>(
    await request.post('/api/auth/login', {
      headers: { 'X-CSRF-TOKEN': csrfToken },
      data: credentials,
    }),
    `Signing in as ${credentials.username}`,
  )
  if (!('mfaRequired' in answer)) return answer

  if (!options.totp) {
    throw new Error(`${credentials.username} has two-step verification; no key was given.`)
  }
  const { code, step } = freshTotp(options.totp.sharedKey, options.totp.lastUsedStep)
  const me = await readJson<Me>(
    await request.post('/api/auth/mfa/verify', {
      headers: { 'X-CSRF-TOKEN': answer.csrfToken },
      data: { code },
    }),
    `Verifying the code of ${credentials.username}`,
  )
  options.totp.onUsed?.(step)
  return me
}

/** Signs `page`'s browser context in and opens `/`, which lands on the role's home page. */
export async function loginAs(
  page: Page,
  who: Role | Credentials,
  options: SignInOptions = {},
): Promise<Me> {
  const me = await signIn(page.request, who, options)
  await page.goto('/')
  if (!me.mustChangePassword && !me.mfaSetupRequired) {
    await expect(page).toHaveURL(new RegExp(`${ROLE_HOME[me.role]}$`))
  }
  return me
}

export interface Api {
  get<T>(path: string): Promise<T>
  post<T>(path: string, data?: unknown): Promise<T>
  put<T>(path: string, data?: unknown): Promise<T>
  delete<T>(path: string): Promise<T>
  /** The raw response, for calls whose failure the caller wants to inspect. */
  send(
    method: 'GET' | 'POST' | 'PUT' | 'DELETE',
    path: string,
    data?: unknown,
  ): Promise<APIResponse>
}

/**
 * Authenticated API calls for setup steps, as the session of `target` (a page's browser context or
 * a standalone request context). Mutations carry the CSRF token from `GET /api/auth/me`, which is
 * bound to the signed-in identity (02-api.md section 3). `reauthenticate`, when given, signs in
 * again after a 401 (another test signed the same demo account out) and the call is retried once.
 */
export function api(
  target: Page | APIRequestContext,
  reauthenticate?: () => Promise<unknown>,
): Api {
  const request = 'goto' in target ? target.request : target

  async function csrfToken(): Promise<string> {
    const me = await readJson<Me>(await request.get('/api/auth/me'), 'GET /api/auth/me')
    return me.csrfToken
  }

  async function send(
    method: 'GET' | 'POST' | 'PUT' | 'DELETE',
    path: string,
    data?: unknown,
  ): Promise<APIResponse> {
    const attempt = async () =>
      request.fetch(path, {
        method,
        headers: method === 'GET' ? {} : { 'X-CSRF-TOKEN': await csrfToken() },
        ...(data === undefined ? {} : { data }),
      })
    try {
      const response = await attempt()
      if (response.status() !== 401 || !reauthenticate) return response
    } catch (error) {
      // The token read itself answered 401: the session has ended.
      if (!reauthenticate || !String(error).includes('answered 401')) throw error
    }
    await reauthenticate()
    return attempt()
  }

  async function call<T>(method: 'GET' | 'POST' | 'PUT' | 'DELETE', path: string, data?: unknown) {
    const response = await send(method, path, data)
    if (!response.ok()) {
      throw new Error(`${method} ${path} answered ${response.status()}: ${await response.text()}`)
    }
    const text = await response.text()
    return (text ? JSON.parse(text) : undefined) as T
  }

  return {
    get: (path) => call('GET', path),
    post: (path, data) => call('POST', path, data ?? {}),
    put: (path, data) => call('PUT', path, data ?? {}),
    delete: (path) => call('DELETE', path),
    send,
  }
}

/**
 * Pins the theme for every page this page's context opens from now on: the stored choice
 * (`localStorage['rushday.theme']`, read by public/theme-init.js before first paint) and the
 * `prefers-color-scheme` media feature, so both the explicit and the system path agree.
 */
export async function setTheme(page: Page, theme: 'light' | 'dark'): Promise<void> {
  await page.addInitScript((value) => {
    try {
      window.localStorage.setItem('rushday.theme', value)
    } catch {
      // Storage blocked: the emulated media feature still decides.
    }
  }, theme)
  await page.emulateMedia({ colorScheme: theme })
}

/** Fails when the document is wider than the viewport (05-frontend.md section 9.3). */
export async function expectNoHorizontalScroll(page: Page): Promise<void> {
  const widths = await page.evaluate(() => ({
    scroll: document.documentElement.scrollWidth,
    viewport: window.innerWidth,
  }))
  expect(
    widths.scroll,
    `page width ${widths.scroll}px in a ${widths.viewport}px viewport`,
  ).toBeLessThanOrEqual(widths.viewport)
}

/**
 * A second, signed-out browser context with this project's base URL and screen, for a journey that
 * needs two people at once (an administrator and the account they manage). The storage state is
 * emptied explicitly: `browser.newContext()` inside a test inherits the file's `test.use` options,
 * which for the admin journeys include the root administrator's saved session.
 */
export async function newSession(browser: Browser, testInfo: TestInfo): Promise<BrowserContext> {
  const { baseURL, viewport, userAgent, deviceScaleFactor, isMobile, hasTouch } =
    testInfo.project.use
  return browser.newContext({
    storageState: { cookies: [], origins: [] },
    ...(baseURL ? { baseURL } : {}),
    ...(viewport ? { viewport } : {}),
    ...(userAgent ? { userAgent } : {}),
    ...(deviceScaleFactor ? { deviceScaleFactor } : {}),
    ...(isMobile !== undefined ? { isMobile } : {}),
    ...(hasTouch !== undefined ? { hasTouch } : {}),
  })
}

/**
 * Specs that change shared state irreversibly (`lecturer-marks`, `admin-accounts`, `admin-mfa`) run
 * on desktop-chromium only (05-frontend.md section 13.2): `test.skip(!onDesktop(testInfo))`.
 */
export function onDesktop(testInfo: TestInfo): boolean {
  return testInfo.project.name === 'desktop-chromium'
}

/** Unique enough for usernames and staff numbers across runs on one database. */
export function uniqueDigits(count: number): string {
  let digits = ''
  while (digits.length < count) digits += Math.floor(Math.random() * 10).toString()
  return digits
}

export interface Fixtures {
  /** The demo administrator's API session, for setup and clean-up (signs in again if it ends). */
  adminApi: Api
}

export const test = base.extend<object, Fixtures>({
  adminApi: [
    async ({ playwright }, provide, workerInfo) => {
      const request = await playwright.request.newContext({
        baseURL: workerInfo.project.use.baseURL,
      })
      const reauthenticate = () => signIn(request, 'admin')
      await reauthenticate()
      await provide(api(request, reauthenticate))
      await request.dispose()
    },
    { scope: 'worker' },
  ],
})

export { expect }
