import { useState } from 'react'
import { Send } from 'lucide-react'

import { describeProblem } from '@/api/problem'
import type { SubmitMarksResponse } from '@/api/types/lecturer'
import {
  AlertDialog,
  AlertDialogCancel,
  AlertDialogContent,
  AlertDialogFooter,
  AlertDialogTrigger,
  Button,
  FormError,
  Skeleton,
} from '@/components/ui'
import { toast } from '@/lib/toast'

import { useMissingStudents } from '../hooks/useMarks'
import { useSubmitMarks } from '../hooks/useSubmitMarks'

export interface SubmitDialogProps {
  code: string
  myRole: 'leader' | 'teacher' | null
  leader: string | null
  total: number
  onSubmitted: (response: SubmitMarksResponse) => void
}

/**
 * `POST /api/lecturer/modules/{code}/marks/submit` (02-api.md section 8.4), leader only
 * (05-frontend.md section 10): confirms the count, lists any students still missing a mark and
 * disables submission until every active enrolment has an outcome. A teacher sees only the caption.
 */
export function SubmitDialog({ code, myRole, leader, total, onSubmitted }: SubmitDialogProps) {
  const [open, setOpen] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const missingQuery = useMissingStudents(code, open)
  const submitMutation = useSubmitMarks(code)

  if (myRole !== 'leader') {
    return <p className="text-sm text-muted">Ask {leader ?? 'the module leader'} to submit.</p>
  }

  const missing = missingQuery.data ?? []
  const canSubmit = total > 0 && missingQuery.isSuccess && missing.length === 0

  async function confirmSubmit() {
    setError(null)
    try {
      const response = await submitMutation.mutateAsync()
      toast.success(`Submitted ${response.gradeCount} marks for ${code}.`)
      setOpen(false)
      onSubmitted(response)
    } catch (caught) {
      setError(describeProblem(caught, { code, ...(leader ? { leader } : {}) }).message)
    }
  }

  return (
    <AlertDialog
      open={open}
      onOpenChange={(next) => {
        setOpen(next)
        if (!next) setError(null)
      }}
    >
      <AlertDialogTrigger asChild>
        <Button disabled={total === 0} title={total === 0 ? 'No students enrolled' : undefined}>
          <Send aria-hidden="true" className="size-4" />
          Submit module
        </Button>
      </AlertDialogTrigger>
      {total === 0 && <p className="text-sm text-muted">No students enrolled</p>}
      <AlertDialogContent
        title={`Submit ${total} ${total === 1 ? 'mark' : 'marks'} for ${code}?`}
        description="Marks are locked for editing and go to the academic office for publication."
      >
        {missingQuery.isPending ? (
          <Skeleton className="h-16 w-full" />
        ) : missing.length > 0 ? (
          <div className="space-y-2">
            <p className="text-sm font-medium text-text">
              {missing.length} {missing.length === 1 ? 'student has' : 'students have'} no mark yet:
            </p>
            <ul className="max-h-40 list-disc space-y-1 overflow-y-auto pl-5 text-sm text-text">
              {missing.map((student) => (
                <li key={student.studentNumber}>
                  <span className="font-mono">{student.studentNumber}</span> {student.fullName}
                </li>
              ))}
            </ul>
            <p className="text-sm text-muted">
              Students without a mark can be recorded as Absent or Deferred from the outcome menu;
              the academic office will follow up.
            </p>
          </div>
        ) : null}
        <FormError>{error}</FormError>
        <AlertDialogFooter>
          <AlertDialogCancel />
          <Button
            onClick={() => void confirmSubmit()}
            loading={submitMutation.isPending}
            disabled={!canSubmit}
          >
            Submit module
          </Button>
        </AlertDialogFooter>
      </AlertDialogContent>
    </AlertDialog>
  )
}
