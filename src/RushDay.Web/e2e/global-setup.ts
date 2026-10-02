import { mkdirSync, writeFileSync } from 'node:fs'
import { dirname } from 'node:path'

import { request as playwrightRequest, type FullConfig } from '@playwright/test'

import {
  api,
  readRootAdminSecrets,
  ROOT_ADMIN,
  ROOT_ADMIN_SECRETS,
  ROOT_ADMIN_STATE,
  signIn,
  type Me,
  type RootAdminSecrets,
} from './fixtures.ts'
import { freshTotp } from './totp.ts'

/**
 * Runs once after the web server is up. Prepares the root administrator, the one non-demo
 * administrator of the e2e database (created by the bootstrap start in scripts/e2e.ps1 and the CI job):
 * the forced password change and the forced authenticator setup happen here through the API, and
 * the signed-in session is saved for `admin-accounts.spec.ts` and `admin-mfa.spec.ts`, so no journey
 * has to spend a TOTP step on signing it in. Tolerates a database where an earlier run already did
 * this, as long as that run's secrets file is still there.
 */
export default async function globalSetup(config: FullConfig): Promise<void> {
  const baseURL = config.projects[0]?.use.baseURL
  if (!baseURL) throw new Error('playwright.config.ts sets no baseURL.')

  const request = await playwrightRequest.newContext({ baseURL })
  try {
    const previous = readRootAdminSecrets()
    const secrets: RootAdminSecrets = {
      username: ROOT_ADMIN.username,
      password: ROOT_ADMIN.password,
      sharedKey: previous?.username === ROOT_ADMIN.username ? previous.sharedKey : '',
      lastStep: previous?.lastStep ?? 0,
    }

    let me: Me
    const first = await request.post('/api/auth/login', {
      headers: { 'X-CSRF-TOKEN': await anonymousToken() },
      data: { username: ROOT_ADMIN.username, password: ROOT_ADMIN.bootstrapPassword },
    })
    if (first.ok()) {
      me = (await first.json()) as Me
    } else if (first.status() === 401) {
      // An earlier run on this database already changed the password (and probably set up the
      // authenticator, whose key that run saved).
      try {
        me = await signIn(
          request,
          { username: ROOT_ADMIN.username, password: ROOT_ADMIN.password },
          secrets.sharedKey
            ? {
                totp: {
                  sharedKey: secrets.sharedKey,
                  lastUsedStep: secrets.lastStep,
                  onUsed: (step) => (secrets.lastStep = step),
                },
              }
            : {},
        )
      } catch (error) {
        throw new Error(
          `${ROOT_ADMIN.username} no longer takes its bootstrap password and an earlier run's ` +
            `password or key (${ROOT_ADMIN_SECRETS}) did not sign it in either. Rebuild the e2e ` +
            `database: scripts/e2e.ps1 drops and recreates it. (${String(error)})`,
          { cause: error },
        )
      }
    } else {
      throw new Error(
        `Signing in as ${ROOT_ADMIN.username} answered ${first.status()}: ${await first.text()}. ` +
          'Was the bootstrap start (demo off, Bootstrap__AdminPassword set) run on this database?',
      )
    }

    const session = api(request)
    if (me.mustChangePassword) {
      await session.post('/api/auth/change-password', {
        currentPassword: ROOT_ADMIN.bootstrapPassword,
        newPassword: ROOT_ADMIN.password,
      })
      me = await session.get<Me>('/api/auth/me')
    }
    if (me.mfaSetupRequired) {
      const setup = await session.post<{ sharedKey: string }>('/api/auth/mfa/setup')
      secrets.sharedKey = setup.sharedKey
      const { code, step } = freshTotp(setup.sharedKey, secrets.lastStep)
      me = await session.post<Me>('/api/auth/mfa/enable', { code })
      secrets.lastStep = step
      // Kept at once: without the key a later run on this database could not sign the account in.
      mkdirSync(dirname(ROOT_ADMIN_SECRETS), { recursive: true })
      writeFileSync(ROOT_ADMIN_SECRETS, JSON.stringify(secrets, null, 2))
    }
    if (me.role !== 'Admin' || me.isDemo || me.mustChangePassword || me.mfaSetupRequired) {
      throw new Error(
        `${ROOT_ADMIN.username} is not a usable real administrator: ${JSON.stringify(me)}`,
      )
    }

    mkdirSync(dirname(ROOT_ADMIN_STATE), { recursive: true })
    await request.storageState({ path: ROOT_ADMIN_STATE })
    writeFileSync(ROOT_ADMIN_SECRETS, JSON.stringify(secrets, null, 2))
  } finally {
    await request.dispose()
  }

  async function anonymousToken(): Promise<string> {
    const response = await request.get('/api/auth/csrf')
    if (!response.ok()) throw new Error(`GET /api/auth/csrf answered ${response.status()}`)
    return ((await response.json()) as { csrfToken: string }).csrfToken
  }
}
