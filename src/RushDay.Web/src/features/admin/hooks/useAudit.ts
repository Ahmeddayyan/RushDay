import { keepPreviousData, useQuery } from '@tanstack/react-query'

import { exportAudit, getAudit } from '@/api/endpoints/admin'
import { queryKeys } from '@/api/keys'
import type { AuditQuery } from '@/api/types/admin'
import { downloadBlob, filenameFromDisposition } from '@/lib/download'

export const AUDIT_PAGE_SIZE = 50

/** `['admin','audit', params]`, 50 per page, newest first. */
export function useAudit(query: AuditQuery) {
  const params = {
    actor: query.actor ?? '',
    action: query.action ?? '',
    studentNumber: query.studentNumber ?? '',
    moduleCode: query.moduleCode ?? '',
    from: query.from ?? '',
    to: query.to ?? '',
    page: query.page ?? 1,
  }
  return useQuery({
    queryKey: queryKeys.admin.audit(params),
    queryFn: () =>
      getAudit({
        ...(params.actor ? { actor: params.actor } : {}),
        ...(params.action ? { action: params.action } : {}),
        ...(params.studentNumber ? { studentNumber: params.studentNumber } : {}),
        ...(params.moduleCode ? { moduleCode: params.moduleCode } : {}),
        ...(params.from ? { from: params.from } : {}),
        ...(params.to ? { to: params.to } : {}),
        page: params.page,
        pageSize: AUDIT_PAGE_SIZE,
      }),
    placeholderData: keepPreviousData,
  })
}

export const EXPORT_CAP_NARROW = 50_000
export const EXPORT_CAP_WIDE = 10_000
const THIRTY_ONE_DAYS_MS = 31 * 24 * 60 * 60 * 1000

/**
 * The server's row cap for a filter set (02-api.md section 8.5): 50,000 when `from` and `to` are
 * both given and at most 31 days apart, otherwise 10,000.
 */
export function exportCap(query: AuditQuery): number {
  if (!query.from || !query.to) return EXPORT_CAP_WIDE
  const span = Date.parse(query.to) - Date.parse(query.from)
  return span >= 0 && span <= THIRTY_ONE_DAYS_MS ? EXPORT_CAP_NARROW : EXPORT_CAP_WIDE
}

export interface AuditExportResult {
  truncated: boolean
  /** The cap the server stopped at, from its final `# truncated at {cap} rows` line when present. */
  cap: number
}

/** Downloads the CSV for these filters and says whether the server stopped at its row cap. */
export async function downloadAuditCsv(query: AuditQuery): Promise<AuditExportResult> {
  const { blob, headers } = await exportAudit(query)
  downloadBlob(blob, filenameFromDisposition(headers, 'audit.csv'))
  const truncated = headers.get('X-RushDay-Truncated')?.toLowerCase() === 'true'
  let cap = exportCap(query)
  if (truncated) {
    const text = await blob.text()
    const match = /#\s*truncated at ([\d,]+) rows/i.exec(text.slice(-200))
    if (match?.[1]) cap = Number(match[1].replaceAll(',', ''))
  }
  return { truncated, cap }
}
