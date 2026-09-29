import type { PublicationInfo } from '@/api/types/common'
import { formatNumber, formatSemester } from '@/lib/format'
import { toast } from '@/lib/toast'

import { useUnpublish } from '../hooks/useResults'

import { ReasonDialog } from './ReasonDialog'

/** `UnpublishDialog`: a live publication is taken back, with a reason. */
export function UnpublishDialog({
  publication,
  onOpenChange,
}: {
  /** Null closes the dialog. */
  publication: PublicationInfo | null
  onOpenChange: (open: boolean) => void
}) {
  const unpublish = useUnpublish()
  const name = publication
    ? `${formatSemester(publication.semester)} ${publication.academicYear}`
    : ''
  return (
    <ReasonDialog
      open={publication !== null}
      onOpenChange={onOpenChange}
      title={`Unpublish ${name} results?`}
      description={`Students stop seeing these ${formatNumber(publication?.gradeCount ?? 0)} marks immediately.`}
      confirmLabel="Unpublish"
      reasonHint="For example the exam board's decision. Recorded in the audit log."
      onConfirm={async (reason) => {
        if (!publication) return
        const result = await unpublish.mutateAsync({ id: publication.id, reason })
        toast.success(
          `Unpublished. ${formatNumber(result.grades)} marks of ${name} are back to Submitted.`,
        )
      }}
    />
  )
}
