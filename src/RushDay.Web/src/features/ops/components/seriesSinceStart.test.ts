import { describe, expect, it } from 'vitest'

import type { OpsSeriesPoint } from '@/api/types/ops'

import { sinceStart } from './seriesSinceStart'

function point(minute: string, requests: number): OpsSeriesPoint {
  return { minute, requests, p95Ms: 10, p99Ms: 20, status5xx: 0, shed503: 0, rateLimited429: 0 }
}

const series = [
  point('2026-09-29T12:40:00.000Z', 0),
  point('2026-09-29T12:41:00.000Z', 0),
  point('2026-09-29T12:42:00.000Z', 120),
  point('2026-09-29T12:43:00.000Z', 240),
  point('2026-09-29T12:44:00.000Z', 3),
]

describe('sinceStart', () => {
  it('starts at the minute the server started and leaves out the minute in progress', () => {
    const kept = sinceStart(series, '2026-09-29T12:42:31.000Z', '2026-09-29T12:44:05.000Z')
    expect(kept.map((p) => p.minute)).toEqual([
      '2026-09-29T12:42:00.000Z',
      '2026-09-29T12:43:00.000Z',
    ])
  })

  it('keeps everything when the instants are unknown', () => {
    expect(sinceStart(series, undefined)).toHaveLength(series.length)
  })
})
