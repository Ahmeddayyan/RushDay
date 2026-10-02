import { useState } from 'react'
import { FlaskConical, KeyRound, UserPlus } from 'lucide-react'
import { Link } from 'react-router'

import { usePublicStatus } from '@/api/endpoints/public'
import type { AccountView, Role } from '@/api/types/common'
import {
  Badge,
  Button,
  Card,
  EmptyState,
  ErrorState,
  FormField,
  PageHeader,
  Pagination,
  Refetching,
  SearchInput,
  Select,
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeaderCell,
  TableRow,
} from '@/components/ui'
import { formatDateTime, formatNumber, formatRole } from '@/lib/format'
import { useDebouncedValue } from '@/lib/useDebouncedValue'
import { useDocumentTitle } from '@/lib/useDocumentTitle'

import { AccountRowActions } from './components/AccountRowActions'
import { ProvisionAccountDialog } from './components/ProvisionAccountDialog'
import { AccountStateChips } from './components/StatusChips'
import { TableSkeleton } from './components/TableSkeleton'
import { ACCOUNTS_PAGE_SIZE, useAccounts } from './hooks/useAccounts'
import { useInstitutionClock } from './lib/useInstitutionClock'
import { useOneShotFlag, useUrlFilters } from './lib/urlState'

const FILTERS = ['q', 'role', 'state'] as const

const ROLE_FILTER = [
  { value: '', label: 'All roles' },
  { value: 'Student', label: 'Students' },
  { value: 'Lecturer', label: 'Lecturers' },
  { value: 'Admin', label: 'Administrators' },
]

const STATE_FILTER = [
  { value: '', label: 'All states' },
  { value: 'active', label: 'Active' },
  { value: 'locked', label: 'Locked' },
  { value: 'disabled', label: 'Disabled' },
]

function asRole(value: string): Role | undefined {
  return value === 'Student' || value === 'Lecturer' || value === 'Admin' ? value : undefined
}

function asState(value: string): AccountView['state'] | undefined {
  return value === 'active' || value === 'locked' || value === 'disabled' ? value : undefined
}

function LinkedNumber({ account }: { account: AccountView }) {
  if (account.studentNumber) {
    return (
      <Link
        to={`/admin/students/${account.studentNumber}`}
        className="font-mono text-primary underline-offset-2 hover:underline"
      >
        {account.studentNumber}
      </Link>
    )
  }
  if (account.staffNumber) return <span className="font-mono">{account.staffNumber}</span>
  return (
    <span className="text-muted">
      <span aria-hidden="true">–</span>
      <span className="sr-only">None</span>
    </span>
  )
}

function AccountsTable({ accounts, timeZone }: { accounts: AccountView[]; timeZone: string }) {
  return (
    <Table caption="Accounts" captionHidden mode="x" density="compact">
      <TableHead>
        <TableRow>
          <TableHeaderCell>Username</TableHeaderCell>
          <TableHeaderCell>Display name</TableHeaderCell>
          <TableHeaderCell>Role</TableHeaderCell>
          <TableHeaderCell>Linked number</TableHeaderCell>
          <TableHeaderCell>State</TableHeaderCell>
          <TableHeaderCell>Last sign-in</TableHeaderCell>
          <TableHeaderCell>
            <span className="sr-only">Actions</span>
          </TableHeaderCell>
        </TableRow>
      </TableHead>
      <TableBody>
        {accounts.map((account) => (
          <TableRow key={account.id}>
            <TableCell className="font-mono whitespace-nowrap">{account.username}</TableCell>
            <TableCell>{account.displayName}</TableCell>
            <TableCell>
              <Badge variant="neutral">{formatRole(account.role)}</Badge>
            </TableCell>
            <TableCell>
              <LinkedNumber account={account} />
            </TableCell>
            <TableCell className="min-w-48">
              <AccountStateChips account={account} timeZone={timeZone} />
            </TableCell>
            <TableCell className="whitespace-nowrap">
              {account.lastLoginAt ? (
                formatDateTime(account.lastLoginAt, timeZone, { zone: false })
              ) : (
                <span className="text-muted">Never</span>
              )}
            </TableCell>
            <TableCell className="text-right">
              <AccountRowActions account={account} />
            </TableCell>
          </TableRow>
        ))}
      </TableBody>
    </Table>
  )
}

/**
 * `/admin/accounts` (05-frontend.md section 10): search, role and state filters in the URL, 25 per
 * page, state chips, row actions, and "Provision account" with the temporary password shown once.
 */
export function Component() {
  useDocumentTitle('Accounts · RushDay')
  const { timeZone } = useInstitutionClock()
  const { data: status } = usePublicStatus()
  const { values, page, update } = useUrlFilters(FILTERS)
  // The overview's "Provision account" quick action arrives as ?provision=1.
  const [provisionOpen, setProvisionOpen] = useState(useOneShotFlag('provision'))
  const q = useDebouncedValue(values.q, 250)
  const role = asRole(values.role)
  const state = asState(values.state)
  const query = useAccounts({
    q,
    ...(role ? { role } : {}),
    ...(state ? { state } : {}),
    page,
  })
  const filtered = Boolean(values.q || role || state)

  let content
  if (query.isPending) {
    content = <TableSkeleton label="accounts" />
  } else if (query.isError) {
    content = (
      <Card>
        <ErrorState error={query.error} onRetry={() => void query.refetch()} />
      </Card>
    )
  } else if (query.data.items.length === 0) {
    content = (
      <Card>
        <EmptyState
          icon={KeyRound}
          title="No accounts match these filters."
          description={
            filtered
              ? 'Try a different search, role or state.'
              : 'Provision an account for a student, a lecturer or a new administrator.'
          }
          action={
            filtered ? (
              <Button
                variant="secondary"
                onClick={() => update({ q: null, role: null, state: null })}
              >
                Clear filters
              </Button>
            ) : (
              <Button onClick={() => setProvisionOpen(true)}>Provision account</Button>
            )
          }
        />
      </Card>
    )
  } else {
    content = (
      <div className="flex flex-col gap-4">
        <Refetching active={query.isPlaceholderData}>
          <AccountsTable accounts={query.data.items} timeZone={timeZone} />
        </Refetching>
        <Pagination
          page={query.data.page}
          pageSize={query.data.pageSize || ACCOUNTS_PAGE_SIZE}
          total={query.data.total}
          onPageChange={(next) => update({ page: next })}
          itemLabel="accounts"
          busy={query.isPlaceholderData}
        />
      </div>
    )
  }

  return (
    <>
      <PageHeader
        title="Accounts"
        description="Sign-ins for students, lecturers and administrators."
        actions={
          <Button onClick={() => setProvisionOpen(true)}>
            <UserPlus aria-hidden="true" className="size-4" />
            Provision account
          </Button>
        }
      />

      {status?.demo && (
        <div className="mb-6 flex items-start gap-3 rounded-lg border border-info/30 bg-info-soft px-4 py-3 text-sm text-info">
          <FlaskConical aria-hidden="true" className="mt-0.5 size-4 shrink-0" />
          <p>
            Demo mode is on: {formatNumber(status.demo.accounts.length)} demo accounts use published
            passwords. They are disabled automatically on the first start with Demo__Enabled off.
          </p>
        </div>
      )}

      <div className="mb-4 flex flex-wrap items-end gap-3">
        <SearchInput
          label="Search accounts"
          showLabel
          value={values.q}
          onChange={(next) => update({ q: next })}
          placeholder="Username or name"
          className="w-full sm:w-72"
        />
        <FormField label="Role" className="w-full sm:w-44">
          <Select
            value={values.role}
            onChange={(event) => update({ role: event.target.value })}
            options={ROLE_FILTER}
          />
        </FormField>
        <FormField label="State" className="w-full sm:w-44">
          <Select
            value={values.state}
            onChange={(event) => update({ state: event.target.value })}
            options={STATE_FILTER}
          />
        </FormField>
      </div>

      {content}

      <ProvisionAccountDialog open={provisionOpen} onOpenChange={setProvisionOpen} />
    </>
  )
}
