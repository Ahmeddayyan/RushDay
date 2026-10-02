import type { OpsBackfill } from '@/api/types/ops'
import { Table, TableBody, TableCell, TableHead, TableHeaderCell, TableRow } from '@/components/ui'
import { formatDateTime, formatNumber } from '@/lib/format'

/**
 * Known backfill step names get a plain-language label (05-frontend.md section 10 example:
 * "Freed CS3099's places for the demo" for `demo_reset_hot_module`); an unrecognised key still shows
 * something readable by humanising the snake_case name, so a future backfill this stage doesn't know
 * about never renders as a bare, meaningless key.
 */
const KNOWN_LABELS: Record<string, string> = {
  demo_reset_hot_module: "Freed CS3099's places for the demo",
  reconcile_enrolled_count: 'Recounted module places against active enrolments',
}

function humanise(key: string): string {
  return KNOWN_LABELS[key] ?? key.replaceAll('_', ' ').replace(/^./, (c) => c.toUpperCase())
}

export interface BackfillsTableProps {
  backfills: OpsBackfill[]
}

/** The backfills history inside `TechnicalDetails` (05-frontend.md section 10). */
export function BackfillsTable({ backfills }: BackfillsTableProps) {
  if (backfills.length === 0) {
    return <p className="text-sm text-muted">No backfills have run since the server started.</p>
  }

  return (
    <Table caption="Backfills" captionHidden>
      <TableHead>
        <TableRow>
          <TableHeaderCell>Step</TableHeaderCell>
          <TableHeaderCell>Completed</TableHeaderCell>
          <TableHeaderCell numeric>Rows affected</TableHeaderCell>
          <TableHeaderCell>Notes</TableHeaderCell>
        </TableRow>
      </TableHead>
      <TableBody>
        {backfills.map((backfill) => (
          <TableRow key={`${backfill.name}-${backfill.completedAt}`}>
            <TableCell label="Step">
              <div>
                <div>{humanise(backfill.name)}</div>
                <div className="font-mono text-xs text-subtle">{backfill.name}</div>
              </div>
            </TableCell>
            <TableCell label="Completed">{formatDateTime(backfill.completedAt)}</TableCell>
            <TableCell numeric label="Rows affected">
              {formatNumber(backfill.rowsAffected)}
            </TableCell>
            <TableCell label="Notes">{backfill.notes ?? '—'}</TableCell>
          </TableRow>
        ))}
      </TableBody>
    </Table>
  )
}
