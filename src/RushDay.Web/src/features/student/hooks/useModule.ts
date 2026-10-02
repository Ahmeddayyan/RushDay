import { useQuery } from '@tanstack/react-query'

import { moduleQueries } from '@/api/endpoints/modules'
import { isModuleCode } from '@/lib/moduleCode'

/**
 * `['modules', code]` (`GET /api/modules/{code}`, 5 s fresh): the live row, polled every 10 s while
 * enrolment for its semester is open and the tab is visible. A code that cannot exist (the route
 * constraint is `^[A-Z]{2}\d{4}$`) is never requested.
 */
export function useModule(code: string) {
  return useQuery({ ...moduleQueries.detail(code), enabled: isModuleCode(code) })
}
