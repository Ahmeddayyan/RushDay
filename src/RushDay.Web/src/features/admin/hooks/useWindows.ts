import { useMutation, useQuery } from '@tanstack/react-query'

import { createWindow, deleteWindow, getWindows, updateWindow } from '@/api/endpoints/admin'
import { queryKeys } from '@/api/keys'
import type { CreateWindowRequest, UpdateWindowRequest } from '@/api/types/admin'

import { adminGroups, useAdminInvalidate } from './invalidate'

/** `['admin','windows']`: every window, newest year first. */
export function useWindows() {
  return useQuery({ queryKey: queryKeys.admin.windows, queryFn: getWindows })
}

/** Windows also reach students through `['public','status']`, so that is refreshed with them. */
function useWindowInvalidate() {
  const invalidate = useAdminInvalidate()
  return () => invalidate(adminGroups.windows, queryKeys.publicStatus)
}

export function useCreateWindow() {
  const invalidate = useWindowInvalidate()
  return useMutation({
    mutationFn: (body: CreateWindowRequest) => createWindow(body),
    onSuccess: invalidate,
  })
}

export function useUpdateWindow() {
  const invalidate = useWindowInvalidate()
  return useMutation({
    mutationFn: ({ id, body }: { id: string; body: UpdateWindowRequest }) => updateWindow(id, body),
    onSuccess: invalidate,
  })
}

export function useDeleteWindow() {
  const invalidate = useWindowInvalidate()
  return useMutation({
    mutationFn: (id: string) => deleteWindow(id),
    onSuccess: invalidate,
  })
}
