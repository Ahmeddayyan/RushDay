import { useMutation, useQueryClient } from '@tanstack/react-query'

import { submitMarks } from '@/api/endpoints/lecturer'
import { queryKeys } from '@/api/keys'

/**
 * `POST /api/lecturer/modules/{code}/marks/submit` (02-api.md section 8.4): leader only. On success
 * every query of this module (roster, marks, the header row in the module list) is invalidated, since
 * the module's status and every row's `gradeStatus` just changed.
 */
export function useSubmitMarks(code: string) {
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: () => submitMarks(code),
    onSuccess: async () => {
      await Promise.all([
        queryClient.invalidateQueries({ queryKey: queryKeys.lecturer.module(code) }),
        queryClient.invalidateQueries({ queryKey: queryKeys.lecturer.modules }),
      ])
    },
  })
}
