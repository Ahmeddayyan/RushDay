import { useId, useState } from 'react'
import { Plus, Trash2, UsersRound } from 'lucide-react'

import { describeProblem } from '@/api/problem'
import type { AdminLecturer, LecturerAssignment } from '@/api/types/admin'
import type { Lecturer } from '@/api/types/common'
import {
  Badge,
  Button,
  Card,
  CardDescription,
  CardHeader,
  CardTitle,
  Combobox,
  FormError,
  FormField,
  Select,
} from '@/components/ui'
import { toast } from '@/lib/toast'
import { useDebouncedValue } from '@/lib/useDebouncedValue'
import { useDirtyForm } from '@/lib/useDirtyForm'

import { useLecturers } from '../hooks/useLecturers'
import { useSetLecturers } from '../hooks/useModules'
import { assignmentProblems, type AssignmentRow } from '../lib/marks'

type Row = AssignmentRow

let nextKey = 1

function rowsFrom(lecturers: Lecturer[]): Row[] {
  return lecturers.map((lecturer) => ({
    key: nextKey++,
    lecturer: {
      staffNumber: lecturer.staffNumber,
      name: `${lecturer.title} ${lecturer.fullName}`,
      left: lecturer.left,
    },
    role: lecturer.role,
  }))
}

function LecturerPicker({
  row,
  label,
  onChange,
}: {
  row: Row
  label: string
  onChange: (lecturer: AdminLecturer | null) => void
}) {
  const [text, setText] = useState(
    row.lecturer ? `${row.lecturer.staffNumber} ${row.lecturer.name}` : '',
  )
  const [chosen, setChosen] = useState<AdminLecturer | null>(null)
  const query = useDebouncedValue(chosen || row.lecturer ? '' : text, 250)
  const lecturers = useLecturers(query)
  // Lecturers who have left can't be assigned (02-api.md section 8.5), so they are not offered.
  const items = (lecturers.data ?? []).filter((item) => item.leftAt === null)

  return (
    <FormField label={label} hideLabel>
      <Combobox
        items={items}
        getKey={(item) => item.staffNumber}
        getLabel={(item) => `${item.staffNumber} ${item.title} ${item.fullName}`}
        renderItem={(item) => (
          <span className="flex flex-col">
            <span>
              <span className="font-mono">{item.staffNumber}</span> {item.title} {item.fullName}
            </span>
            <span className="text-xs text-muted">
              {item.department}
              {item.moduleCodes.length > 0 && ` · ${item.moduleCodes.join(', ')}`}
            </span>
          </span>
        )}
        value={chosen}
        onChange={(item) => {
          setChosen(item)
          onChange(item)
        }}
        inputValue={text}
        onInputChange={(value) => {
          setText(value)
          if (row.lecturer && chosen === null) onChange(null)
        }}
        loading={lecturers.isFetching}
        placeholder="Staff number or name"
        emptyState={
          query.trim()
            ? `No current lecturers match "${query.trim()}".`
            : 'Type to search lecturers.'
        }
      />
    </FormField>
  )
}

/**
 * `LecturerAssignmentEditor` (05-frontend.md section 10, `/admin/modules/:code` Details): rows of a
 * staff-number combobox (lecturers who have left are not offered) and a role; exactly one leader,
 * enforced here and by the server.
 */
export function LecturerAssignmentEditor({
  code,
  lecturers,
}: {
  code: string
  lecturers: Lecturer[]
}) {
  const save = useSetLecturers(code)
  const [rows, setRows] = useState<Row[]>(() => rowsFrom(lecturers))
  const [dirty, setDirty] = useState(false)
  const [attempted, setAttempted] = useState(false)
  const [serverError, setServerError] = useState<string | null>(null)
  const headingId = useId()
  useDirtyForm(dirty)

  const problems = assignmentProblems(rows)

  function change(next: Row[]) {
    setRows(next)
    setDirty(true)
    setServerError(null)
  }

  function updateRow(key: number, patch: Partial<Row>) {
    change(rows.map((row) => (row.key === key ? { ...row, ...patch } : row)))
  }

  async function submit() {
    setAttempted(true)
    if (problems.length > 0) return
    const assignments: LecturerAssignment[] = rows.flatMap((row) =>
      row.lecturer ? [{ staffNumber: row.lecturer.staffNumber, role: row.role }] : [],
    )
    try {
      const detail = await save.mutateAsync(assignments)
      setRows(rowsFrom(detail.lecturers))
      setDirty(false)
      setAttempted(false)
      toast.success(`Saved the lecturers of ${code}.`)
    } catch (error) {
      setServerError(describeProblem(error, { code }).message)
    }
  }

  return (
    <Card>
      <section aria-labelledby={headingId}>
        <CardHeader>
          <CardTitle id={headingId}>Lecturers</CardTitle>
          <CardDescription>
            Exactly one leader, who submits the marks; teachers enter marks too.
          </CardDescription>
        </CardHeader>
        {rows.length === 0 ? (
          <p className="mb-4 flex items-center gap-2 text-sm text-muted">
            <UsersRound aria-hidden="true" className="size-4" />
            No lecturers are assigned yet.
          </p>
        ) : (
          <ul className="mb-4 flex flex-col gap-3" aria-label={`Lecturers of ${code}`}>
            {rows.map((row, index) => (
              <li
                key={row.key}
                className="grid gap-2 rounded-md border border-border p-3 sm:grid-cols-[1fr_10rem_auto] sm:items-start"
              >
                <div className="flex flex-col gap-1">
                  <LecturerPicker
                    row={row}
                    label={`Lecturer ${index + 1}`}
                    onChange={(lecturer) =>
                      updateRow(row.key, {
                        lecturer: lecturer
                          ? {
                              staffNumber: lecturer.staffNumber,
                              name: `${lecturer.title} ${lecturer.fullName}`,
                              left: lecturer.leftAt !== null,
                            }
                          : null,
                      })
                    }
                  />
                  {row.lecturer?.left && (
                    <span>
                      <Badge variant="warning">Left: remove or replace</Badge>
                    </span>
                  )}
                </div>
                <FormField label={`Role of lecturer ${index + 1}`} hideLabel>
                  <Select
                    value={row.role}
                    onChange={(event) =>
                      updateRow(row.key, { role: event.target.value as Row['role'] })
                    }
                    options={[
                      { value: 'leader', label: 'Leader' },
                      { value: 'teacher', label: 'Teacher' },
                    ]}
                  />
                </FormField>
                <Button
                  variant="ghost"
                  size="icon"
                  aria-label={`Remove ${row.lecturer ? row.lecturer.staffNumber : `lecturer ${index + 1}`}`}
                  onClick={() => change(rows.filter((item) => item.key !== row.key))}
                >
                  <Trash2 aria-hidden="true" className="size-4" />
                </Button>
              </li>
            ))}
          </ul>
        )}
        {attempted && problems.length > 0 && (
          <FormError title="Check the lecturers">
            <ul className="list-disc pl-4">
              {problems.map((problem) => (
                <li key={problem}>{problem}</li>
              ))}
            </ul>
          </FormError>
        )}
        <FormError>{serverError}</FormError>
        <div className="mt-4 flex flex-wrap gap-2">
          <Button
            variant="secondary"
            onClick={() =>
              change([
                ...rows,
                { key: nextKey++, lecturer: null, role: rows.length === 0 ? 'leader' : 'teacher' },
              ])
            }
          >
            <Plus aria-hidden="true" className="size-4" />
            Add lecturer
          </Button>
          <Button onClick={() => void submit()} loading={save.isPending} disabled={!dirty}>
            Save lecturers
          </Button>
        </div>
      </section>
    </Card>
  )
}
