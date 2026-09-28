import { describe, expect, it } from 'vitest'

import { ApiError, NetworkError } from './client'
import { createQueryClient } from './queryClient'

function retryOf(client: ReturnType<typeof createQueryClient>) {
  const retry = client.getDefaultOptions().queries?.retry
  if (typeof retry !== 'function') throw new Error('retry is not a function')
  return retry as (failureCount: number, error: unknown) => boolean
}

function retryDelayOf(client: ReturnType<typeof createQueryClient>) {
  const retryDelay = client.getDefaultOptions().queries?.retryDelay
  if (typeof retryDelay !== 'function') throw new Error('retryDelay is not a function')
  return retryDelay as (failureCount: number, error: unknown) => number
}

describe('createQueryClient', () => {
  it('never retries a 4xx, including 429', () => {
    const retry = retryOf(createQueryClient())
    expect(retry(0, new ApiError(404, undefined, undefined))).toBe(false)
    expect(retry(0, new ApiError(429, undefined, undefined))).toBe(false)
  })

  it('retries a 5xx up to twice', () => {
    const retry = retryOf(createQueryClient())
    expect(retry(0, new ApiError(503, undefined, undefined))).toBe(true)
    expect(retry(2, new ApiError(503, undefined, undefined))).toBe(false)
  })

  it('retries a network error', () => {
    const retry = retryOf(createQueryClient())
    expect(retry(0, new NetworkError(new Error('offline')))).toBe(true)
  })

  it('honours Retry-After in retryDelay, plus jitter', () => {
    const retryDelay = retryDelayOf(createQueryClient())
    const headers = new Headers({ 'Retry-After': '2' })
    const delay = retryDelay(0, new ApiError(429, undefined, headers))
    expect(delay).toBeGreaterThanOrEqual(2000)
    expect(delay).toBeLessThan(3000)
  })

  it('falls back to capped exponential backoff with jitter otherwise', () => {
    const retryDelay = retryDelayOf(createQueryClient())
    const delay = retryDelay(10, new Error('boom'))
    expect(delay).toBeGreaterThanOrEqual(8000)
    expect(delay).toBeLessThan(9000)
  })

  it('never retries mutations', () => {
    expect(createQueryClient().getDefaultOptions().mutations?.retry).toBe(0)
  })
})
