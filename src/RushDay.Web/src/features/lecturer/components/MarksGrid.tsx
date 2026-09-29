import { useEffect, useMemo, useRef, useState, type ClipboardEvent, type KeyboardEvent, type ReactNode } from 'react'
import { Lock } from 'lucide-react'
import { z } from 'zod'

import type { GradeOutcome, MarksStatusValue } from '@/api/types/common'
import type { MarksRow } from '@/api/types/lecturer'
import {
  Badge,
  Button,
  EmptyState,
  ErrorState,
  LoadingRegion,
  Pagination,
  Refetching,
  SearchInput,
  Select,
  Skeleton,
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeaderCell,
  TableRow,
} from '@/components/ui'
import { controlClassName } from '@/components/ui/field'
import { cn } from '@/lib/cn'
import { formatDateTime } from '@/lib/format'
import { toast } from '@/lib/toast'
import { useDebouncedValue } from '@/lib/useDebouncedValue'
import { useDirtyForm } from '@/lib/useDirtyForm'

import { useMarks } from '../hooks/useMarks'
import { type DirtyMarkRow, useSaveMarks } from '../hooks/useSaveMarks'
import { clearMarksMirror, readMarksMirror, useMarksMirror, type MarksMirror } from '../hooks/useMarksMirror'

const markSchema = z.coerce.number().int().min(0).max(100)

const OUTCOME_OPTIONS = [
  { value: 'mark', label: 'Mark' },
  { value: 'absent', label: 'Absent' },
  { value: 'deferred', label: 'Deferred' },
] as const

export interface MarksGridProps {
  code: string
  /** The module's status from the header (05-frontend.md section 10); the sheet's own status wins once it loads. */
  status: MarksStatusValue
  page: number
  q: string
  onPageChange: (page: number) => void
  onQueryChange: (q: string) => void
  /** The Submit button/dialog, rendered next to Save (leader-only; MarksTab decides). */
  submitSlot?: ReactNode
  timeZone?: string
}

interface RowError {
  message: string
}

function digitsOnly(raw: string): string {
  return raw.replace(/[^0-9]/g, '').slice(0, 3)
}

/**
 * The marks grid (05-frontend.md sections 9.3 and 10, the hardest component in the product): paged,
 * searched, outcome + mark per row, dirty rows tracked across pages, chunked save of dirty rows only,
 * keyboard navigation and paste, stale-mark and not-enrolled handling, a sessionStorage mirror against
 * a session that expires mid-edit, and a beforeunload guard.
 */
export function MarksGrid({
  code,
  status,
  page,
  q,
  onPageChange,
  onQueryChange,
  submitSlot,
  timeZone,
}: MarksGridProps) {
  const debouncedQ = useDebouncedValue(q, 250)
  const sheetQuery = useMarks(code, { q: debouncedQ, page })
  const saveMutation = useSaveMarks(code)

  // Restore a mirror left by a previous, interrupted session, once, from the grid's first render for
  // this module: computed as lazy initial state (never inside an effect) so the grid comes back
  // exactly as the lecturer left it on the very first paint, with Save already enabled.
  const [restored] = useState<{
    dirty: Record<string, DirtyMarkRow>
    markText: Record<string, string>
    count: number
  }>(() => {
    const mirror = readMarksMirror(code)
    if (!mirror) return { dirty: {}, markText: {}, count: 0 }
    const nextDirty: Record<string, DirtyMarkRow> = {}
    const nextText: Record<string, string> = {}
    for (const [studentNumber, entry] of Object.entries(mirror)) {
      nextDirty[studentNumber] = { studentNumber, mark: entry.mark, outcome: entry.outcome, version: entry.version }
      nextText[studentNumber] = entry.outcome === 'mark' && entry.mark !== null ? String(entry.mark) : ''
    }
    return { dirty: nextDirty, markText: nextText, count: Object.keys(nextDirty).length }
  })

  const [dirty, setDirty] = useState<Record<string, DirtyMarkRow>>(restored.dirty)
  const [markText, setMarkText] = useState<Record<string, string>>(restored.markText)
  const [rowErrors, setRowErrors] = useState<Record<string, RowError>>({})
  const [staleNumbers, setStaleNumbers] = useState<Set<string>>(new Set())
  const [restoredCount] = useState<number | null>(restored.count > 0 ? restored.count : null)

  const inputRefs = useRef<Array<HTMLInputElement | null>>([])

  const mirror = useMemo<MarksMirror>(() => {
    const out: MarksMirror = {}
    for (const [studentNumber, row] of Object.entries(dirty)) {
      out[studentNumber] = { mark: row.mark, outcome: row.outcome, version: row.version }
    }
    return out
  }, [dirty])
  useMarksMirror(code, mirror)

  const dirtyCount = Object.keys(dirty).length
  useDirtyForm(dirtyCount > 0)

  // A dirty grid must never lose work to an accidental tab close (05-frontend.md section 10).
  useEffect(() => {
    if (dirtyCount === 0) return
    const handler = (event: BeforeUnloadEvent) => {
      event.preventDefault()
      event.returnValue = ''
    }
    window.addEventListener('beforeunload', handler)
    return () => window.removeEventListener('beforeunload', handler)
  }, [dirtyCount])

  const sheetStatus = sheetQuery.data?.status ?? status
  const locked = sheetStatus !== 'draft' && sheetStatus !== 'noStudents'

  function updateDirty(row: MarksRow, patch: { outcome: GradeOutcome; mark: number | null }) {
    setDirty((prev) => {
      const existing = prev[row.studentNumber]
      return {
        ...prev,
        [row.studentNumber]: {
          studentNumber: row.studentNumber,
          version: existing?.version ?? row.version,
          ...patch,
        },
      }
    })
  }

  function setRowError(studentNumber: string, message: string | null) {
    setRowErrors((prev) => {
      if (message === null) {
        if (!(studentNumber in prev)) return prev
        const next = { ...prev }
        delete next[studentNumber]
        return next
      }
      return { ...prev, [studentNumber]: { message } }
    })
  }

  function applyMarkText(row: MarksRow, raw: string) {
    const digits = digitsOnly(raw)
    setMarkText((prev) => ({ ...prev, [row.studentNumber]: digits }))
    if (digits === '') {
      setRowError(row.studentNumber, 'Enter a mark from 0 to 100.')
      updateDirty(row, { outcome: 'mark', mark: null })
      return
    }
    const parsed = markSchema.safeParse(digits)
    if (!parsed.success) {
      setRowError(row.studentNumber, 'Enter a whole number from 0 to 100.')
      updateDirty(row, { outcome: 'mark', mark: Number(digits) })
      return
    }
    setRowError(row.studentNumber, null)
    updateDirty(row, { outcome: 'mark', mark: parsed.data })
  }

  function handleOutcomeChange(row: MarksRow, value: GradeOutcome) {
    if (value === 'mark') {
      const fallback = row.outcome === 'mark' ? row.mark : null
      setMarkText((prev) => ({ ...prev, [row.studentNumber]: fallback !== null ? String(fallback) : '' }))
      setRowError(row.studentNumber, null)
      updateDirty(row, { outcome: 'mark', mark: fallback })
    } else {
      setMarkText((prev) => ({ ...prev, [row.studentNumber]: '' }))
      setRowError(row.studentNumber, null)
      updateDirty(row, { outcome: value, mark: null })
    }
  }

  const activeRows = useMemo(
    () => (sheetQuery.data?.rows ?? []).filter((row) => row.enrolmentStatus === 'active'),
    [sheetQuery.data],
  )

  function handleKeyDown(event: KeyboardEvent<HTMLInputElement>, activeIndex: number) {
    if (event.key === 'Enter' || event.key === 'ArrowDown') {
      event.preventDefault()
      inputRefs.current[activeIndex + 1]?.focus()
    } else if (event.key === 'ArrowUp') {
      event.preventDefault()
      inputRefs.current[activeIndex - 1]?.focus()
    }
  }

  function handlePaste(event: ClipboardEvent<HTMLInputElement>, activeIndex: number) {
    const text = event.clipboardData.getData('text')
    const lines = text.split(/\r\n|\r|\n/).filter((line) => line.trim() !== '')
    if (lines.length < 2) return // a single value: let the default paste behaviour handle it
    event.preventDefault()
    lines.forEach((line, offset) => {
      const target = activeRows[activeIndex + offset]
      if (target) applyMarkText(target, line)
    })
  }

  async function handleSave() {
    // A previous stale-mark conflict may since have been resolved by a background refetch of this
    // module's marks: reconcile against the freshest known version before sending, so a lecturer who
    // simply presses Save again does not hit the same conflict a second time.
    const freshByNumber = new Map((sheetQuery.data?.rows ?? []).map((row) => [row.studentNumber, row]))
    const rows = Object.values(dirty)
      .filter((row) => !rowErrors[row.studentNumber])
      .map((row) => {
        const fresh = freshByNumber.get(row.studentNumber)
        return fresh && fresh.version !== row.version ? { ...row, version: fresh.version } : row
      })
    if (rows.length === 0) return
    const result = await saveMutation.mutateAsync(rows)

    setDirty((prev) => {
      const next = { ...prev }
      for (const studentNumber of result.savedStudentNumbers) delete next[studentNumber]
      return next
    })
    setMarkText((prev) => {
      const next = { ...prev }
      for (const studentNumber of result.savedStudentNumbers) delete next[studentNumber]
      return next
    })

    if (result.staleStudentNumbers.length > 0) {
      setStaleNumbers((prev) => new Set([...prev, ...result.staleStudentNumbers]))
      for (const studentNumber of result.staleStudentNumbers) {
        setRowError(studentNumber, 'Someone else changed this mark. Review the highlighted row and save again.')
      }
    }
    if (result.notEnrolledStudentNumbers.length > 0) {
      for (const studentNumber of result.notEnrolledStudentNumbers) {
        setRowError(studentNumber, 'This student is no longer enrolled.')
      }
    }
    if (result.chunkError) {
      for (const studentNumber of result.chunkErrorStudentNumbers) setRowError(studentNumber, result.chunkError)
    }

    if (result.savedStudentNumbers.length > 0) {
      setStaleNumbers((prev) => {
        const next = new Set(prev)
        for (const studentNumber of result.savedStudentNumbers) next.delete(studentNumber)
        return next
      })
      clearMarksMirror(code)
    }

    const saved = result.savedStudentNumbers.length
    const failed = result.requested - saved
    if (failed === 0) {
      toast.success(`Saved ${saved} ${saved === 1 ? 'change' : 'changes'}.`)
      clearMarksMirror(code)
    } else {
      toast.error(`Saved ${saved} of ${result.requested} changes; ${failed} need attention.`)
    }
  }

  let body: ReactNode
  if (sheetQuery.isPending) {
    body = (
      <LoadingRegion label="marks">
        <div className="space-y-2">
          {Array.from({ length: 6 }, (_, index) => (
            <Skeleton key={index} className="h-12 w-full" />
          ))}
        </div>
      </LoadingRegion>
    )
  } else if (sheetQuery.isError) {
    body = <ErrorState error={sheetQuery.error} context={{ code }} onRetry={() => void sheetQuery.refetch()} />
  } else if (sheetQuery.data.rows.length === 0) {
    body = (
      <EmptyState
        title={q ? `No students match "${q}".` : `No students are enrolled on ${code} this year.`}
      />
    )
  } else {
    const sheet = sheetQuery.data
    let activeIndex = -1
    body = (
      <Refetching active={sheetQuery.isFetching}>
        <Table caption={`Marks for ${code}`} captionHidden>
          <TableHead>
            <TableRow>
              <TableHeaderCell>Number</TableHeaderCell>
              <TableHeaderCell>Name</TableHeaderCell>
              <TableHeaderCell>Outcome</TableHeaderCell>
              <TableHeaderCell numeric>Mark</TableHeaderCell>
              <TableHeaderCell>Updated</TableHeaderCell>
            </TableRow>
          </TableHead>
          <TableBody>
            {sheet.rows.map((row) => {
              const isActive = row.enrolmentStatus === 'active'
              const rowIndex = isActive ? ++activeIndex : -1
              const edit = dirty[row.studentNumber]
              const outcomeValue = edit?.outcome ?? row.outcome ?? 'mark'
              const isDirty = Boolean(edit)
              const isStale = staleNumbers.has(row.studentNumber)
              const rowError = rowErrors[row.studentNumber]?.message
              const markValue = markText[row.studentNumber] ?? (row.mark !== null ? String(row.mark) : '')

              return (
                <TableRow
                  key={row.studentNumber}
                  className={cn(
                    !isActive && 'opacity-60',
                    isDirty && 'border-l-4 border-l-primary',
                    isStale && 'border-l-4 border-l-warning bg-warning-soft/40',
                  )}
                  data-dirty={isDirty ? 'true' : undefined}
                  data-stale={isStale ? 'true' : undefined}
                >
                  <TableCell label="Number" className="font-mono">
                    {row.studentNumber}
                  </TableCell>
                  <TableCell label="Name">{row.fullName}</TableCell>
                  <TableCell label="Outcome">
                    {isActive ? (
                      <Select
                        aria-label={`Outcome for ${row.fullName}`}
                        value={outcomeValue}
                        disabled={locked}
                        onChange={(event) =>
                          handleOutcomeChange(row, event.target.value as GradeOutcome)
                        }
                        options={OUTCOME_OPTIONS}
                        className="w-32"
                      />
                    ) : (
                      <span className="text-sm text-muted">Withdrawn: not submitted</span>
                    )}
                  </TableCell>
                  <TableCell label="Mark" numeric>
                    {isActive ? (
                      <div className="flex flex-col items-end gap-1">
                        <div className="flex items-center gap-2">
                          <input
                            ref={(element) => {
                              inputRefs.current[rowIndex] = element
                            }}
                            aria-label={`Mark for ${row.fullName}`}
                            type="text"
                            inputMode="numeric"
                            pattern="[0-9]*"
                            maxLength={3}
                            value={markValue}
                            disabled={locked || outcomeValue !== 'mark'}
                            aria-invalid={rowError ? true : undefined}
                            onChange={(event) => applyMarkText(row, event.target.value)}
                            onKeyDown={(event) => handleKeyDown(event, rowIndex)}
                            onPaste={(event) => handlePaste(event, rowIndex)}
                            className={controlClassName('h-10 w-20 text-right tabular-nums')}
                          />
                          {locked && <Lock aria-hidden="true" className="size-4 shrink-0 text-muted" />}
                        </div>
                        {rowError && (
                          <p role="alert" className="max-w-40 text-right text-xs text-danger">
                            {rowError}
                          </p>
                        )}
                        {isStale && !rowError && (
                          <p className="max-w-40 text-right text-xs text-warning">
                            Server value: {row.mark ?? row.outcome ?? 'none'}
                          </p>
                        )}
                      </div>
                    ) : (
                      <span className="text-sm text-muted">—</span>
                    )}
                  </TableCell>
                  <TableCell label="Updated">
                    {row.updatedAt ? (
                      <span className="text-sm text-muted">
                        {formatDateTime(row.updatedAt, timeZone, { zone: false })}
                        {row.enteredBy && ` · ${row.enteredBy}`}
                      </span>
                    ) : (
                      <span className="text-sm text-muted">—</span>
                    )}
                  </TableCell>
                </TableRow>
              )
            })}
          </TableBody>
        </Table>
      </Refetching>
    )
  }

  return (
    <div className="flex flex-col gap-4">
      <p className="text-sm text-muted">Rows are in student-number order, active students first.</p>
      {restoredCount !== null && restoredCount > 0 && (
        <div role="status" className="rounded-md border border-info/30 bg-info-soft px-3.5 py-2.5 text-sm text-info">
          Restored {restoredCount} unsaved {restoredCount === 1 ? 'mark' : 'marks'} from before you
          were signed out.
        </div>
      )}
      {locked && (
        <div className="rounded-md border border-border bg-surface-2 px-3.5 py-2.5 text-sm text-muted">
          Marks for this module are locked and can&apos;t be edited here.
        </div>
      )}
      <div className="flex flex-wrap items-center gap-3">
        <SearchInput
          label="Search students"
          value={q}
          onChange={onQueryChange}
          placeholder="Number or name"
          className="min-w-56 flex-1"
        />
        <p aria-live="polite" className="text-sm text-muted tabular-nums">
          {dirtyCount > 0 ? `${dirtyCount} unsaved` : 'All changes saved'}
        </p>
        <Button
          onClick={() => void handleSave()}
          disabled={dirtyCount === 0 || locked}
          loading={saveMutation.isPending}
        >
          Save marks
        </Button>
        {submitSlot}
      </div>
      {sheetQuery.data && (
        <div className="flex flex-wrap items-center gap-2 text-sm text-muted">
          <Badge variant="neutral">{sheetQuery.data.summary.entered} entered</Badge>
          <Badge variant={sheetQuery.data.summary.missing > 0 ? 'warning' : 'success'}>
            {sheetQuery.data.summary.missing} missing
          </Badge>
          <Badge variant="neutral">{sheetQuery.data.summary.total} total</Badge>
        </div>
      )}
      {body}
      {sheetQuery.data && sheetQuery.data.total > 0 && (
        <Pagination
          page={sheetQuery.data.page}
          pageSize={sheetQuery.data.pageSize}
          total={sheetQuery.data.total}
          onPageChange={onPageChange}
          itemLabel="students"
          busy={sheetQuery.isFetching}
        />
      )}
    </div>
  )
}
