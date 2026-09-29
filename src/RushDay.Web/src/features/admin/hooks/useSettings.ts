import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'

import { getSettings, updateSettings } from '@/api/endpoints/admin'
import { queryKeys } from '@/api/keys'
import type { UpdateSettingsRequest } from '@/api/types/admin'

import { adminGroups, useAdminInvalidate } from './invalidate'

/** `['admin','settings']` */
export function useAdminSettings() {
  return useQuery({ queryKey: queryKeys.admin.settings, queryFn: getSettings })
}

/**
 * `PUT /api/admin/settings`. The institution name, zone and support details appear in
 * `['public','status']` and a year change moves every area, so those are refreshed too.
 */
export function useUpdateSettings() {
  const queryClient = useQueryClient()
  const invalidate = useAdminInvalidate()
  return useMutation({
    mutationFn: (body: UpdateSettingsRequest) => updateSettings(body),
    onSuccess: async (settings) => {
      queryClient.setQueryData(queryKeys.admin.settings, settings)
      await invalidate(
        adminGroups.settings,
        adminGroups.windows,
        adminGroups.modules,
        adminGroups.results,
        queryKeys.publicStatus,
      )
    },
  })
}
