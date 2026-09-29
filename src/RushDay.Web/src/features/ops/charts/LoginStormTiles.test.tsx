import { screen } from '@testing-library/react'
import { describe, expect, it } from 'vitest'

import { loadResultsHandler, makeLoadResults, makeLoadRun } from '@/test/handlers/ops'
import { renderWithProviders } from '@/test/render'

import { LoginStormTiles } from './LoginStormTiles'

describe('LoginStormTiles', () => {
  it('shows an empty state before any v1 login-storm run exists (v0 had no such scenario)', async () => {
    renderWithProviders(<LoginStormTiles />, {
      handlers: [loadResultsHandler(makeLoadResults({ runs: [makeLoadRun()] }))],
    })
    expect(await screen.findByText('The login storm')).toBeInTheDocument()
    expect(await screen.findByText('Not yet measured')).toBeInTheDocument()
  })

  it('shows guard-mode headline numbers once a run is committed', async () => {
    const guard = makeLoadRun({
      id: 'login-storm-guard',
      scenario: 'login-storm',
      version: 'v1',
      label: 'Login storm (guard mode)',
      mode: 'guard',
      metrics: {
        requests: 3000,
        failedRate: 0.01,
        p50Ms: 20,
        p95Ms: 80,
        maxMs: 200,
        achievedRate: 40,
        loginsOk: 2950,
        loginsRateLimited: 50,
      },
    })
    renderWithProviders(<LoginStormTiles />, {
      handlers: [loadResultsHandler(makeLoadResults({ runs: [guard] }))],
    })

    expect(await screen.findByText('40/s')).toBeInTheDocument()
    expect(screen.getByText('2,950')).toBeInTheDocument()
    expect(screen.getByText('50')).toBeInTheDocument()
  })
})
