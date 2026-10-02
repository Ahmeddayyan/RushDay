import { screen } from '@testing-library/react'
import { describe, expect, it } from 'vitest'

import { makeOpsSnapshot } from '@/test/factories'
import { renderWithProviders } from '@/test/render'

import { HealthSummary, healthState } from './HealthSummary'

describe('healthState', () => {
  it('is "ok" with no errors and nobody asked to try again', () => {
    expect(healthState(makeOpsSnapshot(), undefined)).toBe('ok')
  })

  it('is "busy" when the last minute shed or rate-limited requests', () => {
    const snapshot = makeOpsSnapshot({
      http: {
        inFlight: 1,
        last60s: {
          requests: 100,
          perSecond: 2,
          p50Ms: 10,
          p95Ms: 40,
          p99Ms: 100,
          status2xx: 90,
          status4xx: 10,
          status5xx: 0,
          rateLimited429: 0,
          shed503: 4,
        },
      },
    })
    expect(healthState(snapshot, undefined)).toBe('busy')
  })

  it('is "struggling" on a real 5xx in the last minute', () => {
    const snapshot = makeOpsSnapshot({
      http: {
        inFlight: 1,
        last60s: {
          requests: 100,
          perSecond: 2,
          p50Ms: 10,
          p95Ms: 40,
          p99Ms: 100,
          status2xx: 95,
          status4xx: 4,
          status5xx: 1,
          rateLimited429: 0,
          shed503: 0,
        },
      },
    })
    expect(healthState(snapshot, undefined)).toBe('struggling')
  })

  it('is "struggling" when db.waitTimeoutsTotal grew since the previous sample', () => {
    const previous = makeOpsSnapshot({
      db: { poolMax: 20, poolBusy: 2, poolIdle: 4, pendingRequests: 0, waitTimeoutsTotal: 3 },
    })
    const snapshot = makeOpsSnapshot({
      db: { poolMax: 20, poolBusy: 2, poolIdle: 4, pendingRequests: 0, waitTimeoutsTotal: 5 },
    })
    expect(healthState(snapshot, previous)).toBe('struggling')
  })
})

describe('HealthSummary', () => {
  it('renders the plain-language heading and the last-minute sentence', () => {
    renderWithProviders(<HealthSummary snapshot={makeOpsSnapshot()} previous={undefined} />)
    expect(screen.getByRole('status')).toHaveTextContent('Running normally')
    expect(screen.getByText(/Last minute: 1,200 requests/)).toBeInTheDocument()
  })

  it('greys out and is still readable while stale', () => {
    renderWithProviders(<HealthSummary snapshot={makeOpsSnapshot()} previous={undefined} stale />)
    expect(screen.getByRole('status')).toHaveClass('opacity-60')
  })
})
