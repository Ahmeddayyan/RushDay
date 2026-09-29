import type { PublicationInfo } from '@/api/types/common'
import { formatDateTime, formatNumber, formatSemester } from '@/lib/format'
import { toast } from '@/lib/toast'

import { useCancelPublication } from '../hooks/useResults'

import { ConfirmDialog } from './ConfirmDialog'

/** `CancelPublicationDialog`: a scheduled publication is withdrawn before anyone sees it. */
export function CancelPublicationDialog({
  publication,
  timeZone,
  onOpenChange,
}: {
  /** Null closes the dialog. */
  publication: PublicationInfo | null
  timeZone: string
  onOpenChange: (open: boolean) => void
}) {
  const cancel = useCancelPublication()
  const name = publication
    ? `${formatSemester(publication.semester)} ${publication.academicYear}`
    : ''
  return (
    <ConfirmDialog
      open={publication !== null}
      onOpenChange={onOpenChange}
      title={`Cancel the ${name} publication scheduled for ${
        publication ? formatDateTime(publication.publishAt, timeZone) : ''
      }?`}
      description="Marks go back to Submitted and no student will see them."
      confirmLabel="Cancel publication"
      onConfirm={async () => {
        if (!publication) return
        const result = await cancel.mutateAsync(publication.id)
        toast.success(
          `Cancelled. ${formatNumber(result.grades)} marks of ${name} are back to Submitted.`,
        )
      }}
    />
  )
}
