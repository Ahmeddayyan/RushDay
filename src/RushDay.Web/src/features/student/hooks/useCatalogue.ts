import { useQuery } from '@tanstack/react-query'

import { moduleQueries } from '@/api/endpoints/modules'

/**
 * `['modules','catalogue']` (`GET /api/modules`, 15 s fresh): every active module, viewer-agnostic
 * and cached by the server for 30 s (D13), so its places can lag; module pages show live numbers.
 */
export function useCatalogue() {
  return useQuery(moduleQueries.catalogue())
}
