import type { ReactNode } from 'react'
import { QueryClientProvider, type QueryClient } from '@tanstack/react-query'

import { queryClient as defaultQueryClient } from '@/api/queryClient'
import { Toaster, TooltipProvider } from '@/components/ui'

import { AuthProvider } from './AuthProvider'

export interface ProvidersProps {
  children: ReactNode
  /** Tests pass a fresh client with retries off. */
  client?: QueryClient
}

/** Everything `main.tsx` needs above the router: server state, the session, tooltips and toasts. */
export function Providers({ children, client = defaultQueryClient }: ProvidersProps) {
  return (
    <QueryClientProvider client={client}>
      <TooltipProvider delayDuration={300}>
        <AuthProvider>{children}</AuthProvider>
        <Toaster />
      </TooltipProvider>
    </QueryClientProvider>
  )
}
