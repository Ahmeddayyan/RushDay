import type { ReactNode } from 'react'
import { QueryClientProvider, type QueryClient } from '@tanstack/react-query'

import type { Me } from '@/api/types/common'
import { AuthProvider } from '@/app/AuthProvider'
import { Toaster, TooltipProvider } from '@/components/ui'

export interface TestFrameProps {
  client: QueryClient
  /** The primed session; undefined boots through `GET /api/auth/me` like the real app. */
  user?: Me | null
  children: ReactNode
}

/** The providers of app/providers.tsx, with a per-test QueryClient and an optionally primed session. */
export function TestFrame({ client, user, children }: TestFrameProps) {
  return (
    <QueryClientProvider client={client}>
      <TooltipProvider delayDuration={0}>
        {user === undefined ? (
          <AuthProvider>{children}</AuthProvider>
        ) : (
          <AuthProvider initialSession={{ user }}>{children}</AuthProvider>
        )}
        <Toaster />
      </TooltipProvider>
    </QueryClientProvider>
  )
}
