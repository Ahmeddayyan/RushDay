import { QueryClient } from '@tanstack/react-query'

import { ApiError } from './client'

/**
 * Defaults for every query and mutation (05-frontend.md section 6.1). Never retries a 4xx (so never a
 * 429); retry delay honours the server's Retry-After when given, plus jitter so 20,000 shed clients
 * never retry in lockstep.
 */
export function createQueryClient(): QueryClient {
  return new QueryClient({
    defaultOptions: {
      queries: {
        staleTime: 30_000,
        gcTime: 5 * 60_000,
        refetchOnWindowFocus: false,
        retry: (failureCount, error) =>
          !(error instanceof ApiError && error.status > 0 && error.status < 500) &&
          failureCount < 2,
        retryDelay: (attemptIndex, error) =>
          (error instanceof ApiError && error.retryAfterSeconds
            ? error.retryAfterSeconds * 1000
            : Math.min(1000 * 2 ** attemptIndex, 8000)) +
          Math.random() * 1000,
      },
      mutations: {
        retry: 0,
      },
    },
  })
}

export const queryClient = createQueryClient()
