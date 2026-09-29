import { Fragment, useId, useState } from 'react'
import { ChevronDown, ChevronRight } from 'lucide-react'
import { Link } from 'react-router'

import type { AuditEventView } from '@/api/types/common'
import {
  Button,
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeaderCell,
  TableRow,
} from '@/components/ui'
import { cn } from '@/lib/cn'

import { auditActionLabel } from '../lib/auditActions'

import { Timestamp } from './Timestamp'

const MAX_DEPTH = 4

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value)
}

/** One value of `details`, always as text: React escapes it, so a stored `<script>` stays words. */
function DetailValue({ value, depth }: { value: unknown; depth: number }) {
  if (value === null || value === undefined) return <span className="text-muted">none</span>
  if (Array.isArray(value)) {
    const items = value as unknown[]
    if (items.length === 0) return <span className="text-muted">none</span>
    if (items.every((item) => !isRecord(item) && !Array.isArray(item))) {
      return <span className="break-words">{items.map(scalarText).join(', ')}</span>
    }
    return (
      <ol className="flex list-decimal flex-col gap-1 pl-5">
        {items.map((item, index) => (
          <li key={index}>
            <DetailValue value={item} depth={depth + 1} />
          </li>
        ))}
      </ol>
    )
  }
  if (isRecord(value)) {
    if (depth >= MAX_DEPTH) return <span className="break-words">{JSON.stringify(value)}</span>
    return <DetailsList details={value} depth={depth + 1} />
  }
  return <span className="break-words whitespace-pre-wrap">{scalarText(value)}</span>
}

/** A JSON scalar as words: strings as written, numbers as digits, booleans as yes / no. */
function scalarText(value: unknown): string {
  if (value === null || value === undefined) return 'none'
  if (typeof value === 'string') return value
  if (typeof value === 'number' || typeof value === 'bigint') return value.toString()
  if (typeof value === 'boolean') return value ? 'yes' : 'no'
  return JSON.stringify(value) ?? ''
}

/** `details` as a definition list of text (05-frontend.md section 10, `/admin/audit`). */
export function DetailsList({
  details,
  depth = 0,
}: {
  details: Record<string, unknown>
  depth?: number
}) {
  const entries = Object.entries(details)
  if (entries.length === 0) return <p className="text-sm text-muted">No details recorded.</p>
  return (
    <dl
      className={cn(
        'grid grid-cols-[minmax(6rem,max-content)_1fr] gap-x-4 gap-y-1.5 text-sm',
        depth > 0 && 'border-l border-border pl-3',
      )}
    >
      {entries.map(([key, value]) => (
        <Fragment key={key}>
          <dt className="font-mono text-xs text-muted">{key}</dt>
          <dd className="min-w-0 text-text">
            <DetailValue value={value} depth={depth} />
          </dd>
        </Fragment>
      ))}
    </dl>
  )
}

function subjectText(event: AuditEventView): string {
  return event.subjectId ? `${event.subjectType} ${event.subjectId}` : event.subjectType
}

/**
 * One audit event: time (absolute, relative in a tooltip), actor and role, the action's label,
 * subject, student, module, and a disclosure button that shows `details` underneath as text.
 */
export function AuditRow({
  event,
  timeZone,
  columns,
}: {
  event: AuditEventView
  timeZone: string
  columns: number
}) {
  const [open, setOpen] = useState(false)
  const detailsId = useId()
  const label = auditActionLabel(event.action)

  return (
    <>
      <TableRow>
        <TableCell label="Time" className="whitespace-nowrap">
          <Timestamp iso={event.occurredAt} timeZone={timeZone} />
        </TableCell>
        <TableCell label="Actor">
          {event.actorUsername ? (
            <span className="flex flex-col">
              <span className="font-mono">{event.actorUsername}</span>
              {event.actorRole && <span className="text-xs text-muted">{event.actorRole}</span>}
            </span>
          ) : (
            <span className="text-muted">System</span>
          )}
        </TableCell>
        <TableCell label="Action">
          <span className="flex flex-col">
            <span>{label}</span>
            <span className="font-mono text-xs text-subtle">{event.action}</span>
          </span>
        </TableCell>
        <TableCell label="Subject" className="max-w-56 break-all text-muted">
          {subjectText(event)}
        </TableCell>
        <TableCell label="Student" className="font-mono">
          {event.studentNumber && (
            <Link
              to={`/admin/students/${event.studentNumber}`}
              className="text-primary underline-offset-2 hover:underline"
            >
              {event.studentNumber}
            </Link>
          )}
        </TableCell>
        <TableCell label="Module" className="font-mono">
          {event.moduleCode && (
            <Link
              to={`/admin/modules/${event.moduleCode}`}
              className="text-primary underline-offset-2 hover:underline"
            >
              {event.moduleCode}
            </Link>
          )}
        </TableCell>
        <TableCell className="text-right">
          <Button
            variant="ghost"
            size="sm"
            aria-expanded={open}
            aria-controls={detailsId}
            onClick={() => setOpen((value) => !value)}
          >
            {open ? (
              <ChevronDown aria-hidden="true" className="size-4" />
            ) : (
              <ChevronRight aria-hidden="true" className="size-4" />
            )}
            Details
            <span className="sr-only"> of {label}</span>
          </Button>
        </TableCell>
      </TableRow>
      <TableRow id={detailsId} hidden={!open} className="bg-surface-2/60 hover:bg-surface-2/60">
        <TableCell colSpan={columns} className="max-sm:block">
          {open && (
            <div className="flex flex-col gap-3">
              {event.details ? (
                <DetailsList details={event.details} />
              ) : (
                <p className="text-sm text-muted">No details recorded.</p>
              )}
              {event.requestId && (
                <p className="text-xs text-muted">
                  Request <span className="font-mono">{event.requestId}</span>
                </p>
              )}
            </div>
          )}
        </TableCell>
      </TableRow>
    </>
  )
}

/** The audit table shared by the audit log, the overview's recent activity and a student's record. */
export function AuditTable({
  events,
  timeZone,
  caption,
  captionHidden = true,
}: {
  events: AuditEventView[]
  timeZone: string
  caption: string
  captionHidden?: boolean
}) {
  return (
    <Table caption={caption} captionHidden={captionHidden} mode="x">
      <TableHead>
        <TableRow>
          <TableHeaderCell>Time</TableHeaderCell>
          <TableHeaderCell>Actor</TableHeaderCell>
          <TableHeaderCell>Action</TableHeaderCell>
          <TableHeaderCell>Subject</TableHeaderCell>
          <TableHeaderCell>Student</TableHeaderCell>
          <TableHeaderCell>Module</TableHeaderCell>
          <TableHeaderCell>
            <span className="sr-only">Details</span>
          </TableHeaderCell>
        </TableRow>
      </TableHead>
      <TableBody>
        {events.map((event) => (
          <AuditRow key={event.id} event={event} timeZone={timeZone} columns={7} />
        ))}
      </TableBody>
    </Table>
  )
}
