import { Link } from 'react-router'

import type { AdminResultsModule } from '@/api/types/admin'
import {
  Button,
  MarksStatusChip,
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeaderCell,
  TableRow,
} from '@/components/ui'
import { formatDateTime, formatNumber } from '@/lib/format'

function NextStep({
  module,
  timeZone,
  onReturnToDraft,
}: {
  module: AdminResultsModule
  timeZone: string
  onReturnToDraft: (module: AdminResultsModule) => void
}) {
  const { marks } = module
  const returnButton = (
    <Button
      variant="secondary"
      size="sm"
      aria-label={`Return ${module.code} to draft`}
      onClick={() => onReturnToDraft(module)}
    >
      Return to draft
    </Button>
  )

  switch (marks.status) {
    case 'draft':
      return <span className="text-muted">Waiting for the lecturer</span>
    case 'submitted':
      return marks.missing === 0 ? (
        <span className="font-medium text-success">Ready to publish</span>
      ) : (
        <span className="flex flex-wrap items-center gap-2">
          <span className="text-warning">
            Submitted, {formatNumber(marks.missing)} marks missing (students added after
            submission):
          </span>
          {returnButton}
        </span>
      )
    case 'scheduled':
      return (
        <span className="flex flex-wrap items-center gap-2">
          <span className="text-muted">
            In the publication scheduled for{' '}
            {marks.publishedAt ? formatDateTime(marks.publishedAt, timeZone) : 'later'}:
          </span>
          {returnButton}
        </span>
      )
    case 'published':
      return (
        <span className="text-muted">
          Live: correct single marks from the{' '}
          <Link
            to={`/admin/modules/${module.code}/marks`}
            className="font-medium text-primary underline-offset-2 hover:underline"
          >
            Marks tab
          </Link>
        </span>
      )
    default:
      return <span className="text-muted">No students this year</span>
  }
}

/**
 * `SubmissionProgressTable` (05-frontend.md section 10, `/admin/results`): code, title, leader,
 * enrolled, entered and missing, the marks chip, and what happens next for each state.
 */
export function SubmissionProgressTable({
  modules,
  timeZone,
  caption,
  onReturnToDraft,
}: {
  modules: AdminResultsModule[]
  timeZone: string
  caption: string
  onReturnToDraft: (module: AdminResultsModule) => void
}) {
  return (
    <Table caption={caption} captionHidden>
      <TableHead>
        <TableRow>
          <TableHeaderCell>Code</TableHeaderCell>
          <TableHeaderCell>Title</TableHeaderCell>
          <TableHeaderCell>Leader</TableHeaderCell>
          <TableHeaderCell numeric>Enrolled</TableHeaderCell>
          <TableHeaderCell numeric>Entered / missing</TableHeaderCell>
          <TableHeaderCell>Status</TableHeaderCell>
          <TableHeaderCell>Next step</TableHeaderCell>
        </TableRow>
      </TableHead>
      <TableBody>
        {modules.map((module) => (
          <TableRow key={module.code}>
            <TableCell label="Code" className="font-mono">
              <Link
                to={`/admin/modules/${module.code}/marks`}
                className="text-primary underline-offset-2 hover:underline"
              >
                {module.code}
              </Link>
            </TableCell>
            <TableCell label="Title">{module.title}</TableCell>
            <TableCell label="Leader">{module.leader ?? 'No leader'}</TableCell>
            <TableCell label="Enrolled" numeric>
              {formatNumber(module.enrolledCount)}
            </TableCell>
            <TableCell label="Entered / missing" numeric>
              {formatNumber(module.marks.entered)} / {formatNumber(module.marks.missing)}
            </TableCell>
            <TableCell label="Status">
              <MarksStatusChip
                status={module.marks.status}
                publishedAt={module.marks.publishedAt}
                timeZone={timeZone}
              />
            </TableCell>
            <TableCell label="Next step" className="text-sm">
              <NextStep module={module} timeZone={timeZone} onReturnToDraft={onReturnToDraft} />
            </TableCell>
          </TableRow>
        ))}
      </TableBody>
    </Table>
  )
}
