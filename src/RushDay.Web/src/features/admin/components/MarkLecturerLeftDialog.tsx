import type { AdminLecturer } from '@/api/types/admin'
import { toast } from '@/lib/toast'

import { useMarkLecturerLeft } from '../hooks/useLecturers'

import { ReasonDialog } from './ReasonDialog'

/** "Mark as left" for a lecturer (05-frontend.md section 10, `/admin/lecturers`). */
export function MarkLecturerLeftDialog({
  lecturer,
  onOpenChange,
}: {
  /** Null closes the dialog. */
  lecturer: AdminLecturer | null
  onOpenChange: (open: boolean) => void
}) {
  const leave = useMarkLecturerLeft()
  const name = lecturer ? `${lecturer.title} ${lecturer.fullName}` : ''

  return (
    <ReasonDialog
      open={lecturer !== null}
      onOpenChange={onOpenChange}
      title={`Mark ${name} as left?`}
      description="Disables the account; their module assignments stay and show as left."
      confirmLabel="Mark as left"
      onConfirm={async (reason) => {
        if (!lecturer) return
        await leave.mutateAsync({ staffNumber: lecturer.staffNumber, reason })
        toast.success(`${lecturer.staffNumber} is marked as left.`)
      }}
    />
  )
}
