import type { ReactNode } from 'react'
import { QueryClientProvider } from '@tanstack/react-query'

import { queryClient } from '@/api/queryClient'

import { AuthProvider } from './AuthProvider'

/** Wraps every provider `main.tsx` needs above the router. */
export function Providers({ children }: { children: ReactNode }) {
  return (
    <QueryClientProvider client={queryClient}>
      <AuthProvider>{children}</AuthProvider>
    </QueryClientProvider>
  )
}
