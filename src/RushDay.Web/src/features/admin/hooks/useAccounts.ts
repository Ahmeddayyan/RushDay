import { keepPreviousData, useMutation, useQuery } from '@tanstack/react-query'

import {
  accountAction,
  getAccounts,
  provisionAccount,
  resetAccountPassword,
  type AccountAction,
} from '@/api/endpoints/admin'
import { queryKeys } from '@/api/keys'
import type { AccountsQuery, ProvisionAccountRequest } from '@/api/types/admin'

import { adminGroups, useAdminInvalidate } from './invalidate'

export const ACCOUNTS_PAGE_SIZE = 25

/** `['admin','accounts', params]`, keeping the previous page on screen while the next loads. */
export function useAccounts(query: AccountsQuery) {
  const params = {
    q: query.q ?? '',
    role: query.role ?? null,
    state: query.state ?? null,
    page: query.page ?? 1,
    pageSize: query.pageSize ?? ACCOUNTS_PAGE_SIZE,
  }
  return useQuery({
    queryKey: queryKeys.admin.accounts(params),
    queryFn: () =>
      getAccounts({
        ...(params.q ? { q: params.q } : {}),
        ...(params.role ? { role: params.role } : {}),
        ...(params.state ? { state: params.state } : {}),
        page: params.page,
        pageSize: params.pageSize,
      }),
    placeholderData: keepPreviousData,
  })
}

/** Account state shows on the accounts, students, student and lecturers pages. */
function useAccountInvalidate() {
  const invalidate = useAdminInvalidate()
  return () =>
    invalidate(
      adminGroups.accounts,
      adminGroups.students,
      adminGroups.student,
      adminGroups.lecturers,
    )
}

export function useProvisionAccount() {
  const invalidate = useAccountInvalidate()
  return useMutation({
    mutationFn: (body: ProvisionAccountRequest) => provisionAccount(body),
    onSuccess: invalidate,
  })
}

export function useAccountAction() {
  const invalidate = useAccountInvalidate()
  return useMutation({
    mutationFn: ({ id, action }: { id: string; action: AccountAction }) =>
      accountAction(id, action),
    onSuccess: invalidate,
  })
}

export function useResetPassword() {
  const invalidate = useAccountInvalidate()
  return useMutation({
    mutationFn: (id: string) => resetAccountPassword(id),
    onSuccess: invalidate,
  })
}
