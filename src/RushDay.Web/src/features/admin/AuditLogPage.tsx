import { useState } from 'react'
import { Download, ScrollText } from 'lucide-react'

import type { AuditQuery } from '@/api/types/admin'
import { describeProblem } from '@/api/problem'
import {
  Button,
  Card,
  EmptyState,
  ErrorState,
  PageHeader,
  Pagination,
  Refetching,
} from '@/components/ui'
import { formatNumber } from '@/lib/format'
import { toast } from '@/lib/toast'
import { useDocumentTitle } from '@/lib/useDocumentTitle'

import { AuditFilters } from './components/AuditFilters'
import { AuditTable } from './components/AuditRow'
import { TableSkeleton } from './components/TableSkeleton'
import { AUDIT_PAGE_SIZE, downloadAuditCsv, useAudit } from './hooks/useAudit'
import { AUDIT_FILTER_KEYS } from './lib/auditRange'
import { useInstitutionClock } from './lib/useInstitutionClock'
import { useUrlFilters } from './lib/urlState'

/**
 * `/admin/audit` (05-frontend.md section 10): filters in the URL, 50 rows per page newest first,
 * expandable details rendered as text, and a CSV export of the same filters that says when the
 * server stopped at its row cap.
 */
export function Component() {
  useDocumentTitle('Audit log · RushDay')
  const { timeZone, now } = useInstitutionClock()
  const { values, page, update } = useUrlFilters(AUDIT_FILTER_KEYS)
  const [exporting, setExporting] = useState(false)

  const filters: AuditQuery = {
    ...(values.actor ? { actor: values.actor } : {}),
    ...(values.action ? { action: values.action } : {}),
    ...(values.studentNumber ? { studentNumber: values.studentNumber } : {}),
    ...(values.moduleCode ? { moduleCode: values.moduleCode } : {}),
    ...(values.from ? { from: values.from } : {}),
    ...(values.to ? { to: values.to } : {}),
  }
  const query = useAudit({ ...filters, page })

  async function exportCsv() {
    setExporting(true)
    try {
      const result = await downloadAuditCsv(filters)
      if (result.truncated) {
        toast.info(
          `The export stopped at ${formatNumber(result.cap)} rows. Narrow the date range to 31 days or less to get everything.`,
        )
      } else {
        toast.success('Audit log exported. The export itself is recorded in the audit log.')
      }
    } catch (error) {
      toast.error(describeProblem(error).message)
    } finally {
      setExporting(false)
    }
  }

  let content
  if (query.isPending) {
    content = <TableSkeleton label="audit log" rows={10} />
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
          icon={ScrollText}
          title="Nothing recorded for these filters."
          description="Every change and every bulk read of personal data is recorded here."
        />
      </Card>
    )
  } else {
    content = (
      <div className="flex flex-col gap-4">
        <Refetching active={query.isPlaceholderData}>
          <AuditTable events={query.data.items} timeZone={timeZone} caption="Audit log" />
        </Refetching>
        <Pagination
          page={query.data.page}
          pageSize={query.data.pageSize || AUDIT_PAGE_SIZE}
          total={query.data.total}
          onPageChange={(next) => update({ page: next })}
          itemLabel="events"
          busy={query.isPlaceholderData}
        />
      </div>
    )
  }

  return (
    <>
      <PageHeader
        title="Audit log"
        description="Who changed what, and when. Entries can't be edited or deleted."
        actions={
          <Button variant="secondary" loading={exporting} onClick={() => void exportCsv()}>
            <Download aria-hidden="true" className="size-4" />
            Export CSV (up to 50,000 rows)<span className="sr-only">, downloads a file</span>
          </Button>
        }
      />
      <AuditFilters values={values} timeZone={timeZone} now={now} onChange={update} />
      {content}
    </>
  )
}
