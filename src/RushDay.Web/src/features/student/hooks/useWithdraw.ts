import { useMutation, useQueryClient } from '@tanstack/react-query'

import { withdraw } from '@/api/endpoints/student'
import { queryKeys } from '@/api/keys'
import { describeProblem } from '@/api/problem'
import type { MyEnrolment } from '@/api/types/common'
import { toast } from '@/lib/toast'

interface WithdrawContext {
  previous: MyEnrolment[] | undefined
}

/**
 * Withdrawal from one module (05-frontend.md section 6.3): optimistic, behind `WithdrawDialog`.
 * `onMutate` snapshots `['student','enrolments']` and marks the row withdrawn; `onError` restores
 * the snapshot and says why; `onSettled` refetches the enrolments, the dashboard, the module and the
 * catalogue (the place is released at once on the server).
 */
export function useWithdraw(code: string) {
  const queryClient = useQueryClient()

  return useMutation<void, Error, void, WithdrawContext>({
    mutationKey: ['student', 'withdraw', code],
    mutationFn: () => withdraw(code),
    retry: 0,
    onMutate: async () => {
      await queryClient.cancelQueries({ queryKey: queryKeys.student.enrolments })
      const previous = queryClient.getQueryData<MyEnrolment[]>(queryKeys.student.enrolments)
      const withdrawnAt = new Date().toISOString()
      queryClient.setQueryData<MyEnrolment[]>(queryKeys.student.enrolments, (rows) =>
        rows?.map((row) =>
          row.moduleCode === code && row.status === 'active'
            ? {
                ...row,
                status: 'withdrawn',
                withdrawnAt,
                canWithdraw: false,
                withdrawBlockedReason: null,
              }
            : row,
        ),
      )
      return { previous }
    },
    onError: (error, _variables, context) => {
      if (context?.previous) {
        queryClient.setQueryData(queryKeys.student.enrolments, context.previous)
      }
      toast.error(describeProblem(error, { code }).message)
    },
    onSuccess: () => {
      toast.success(`You've withdrawn from ${code}. Your place has been released.`)
    },
    onSettled: () =>
      Promise.all([
        queryClient.invalidateQueries({ queryKey: queryKeys.student.enrolments }),
        queryClient.invalidateQueries({ queryKey: queryKeys.student.dashboard }),
        queryClient.invalidateQueries({ queryKey: queryKeys.modules.detail(code) }),
        queryClient.invalidateQueries({ queryKey: queryKeys.modules.catalogue }),
      ]),
  })
}
