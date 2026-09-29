import type { AdminResultsModule } from '@/api/types/admin'
import { formatDateTime } from '@/lib/format'
import { toast } from '@/lib/toast'

import { useReturnToDraft } from '../hooks/useResults'

import { ReasonDialog } from './ReasonDialog'

/**
 * `ReturnToDraftDialog` (05-frontend.md section 10): a submitted module, or one in a publication
 * that is still scheduled, goes back to its lecturers with a reason.
 */
export function ReturnToDraftDialog({
  module,
  academicYear,
  timeZone,
  onOpenChange,
}: {
  /** Null closes the dialog. */
  module: AdminResultsModule | null
  academicYear: string
  timeZone: string
  onOpenChange: (open: boolean) => void
}) {
  const returnToDraft = useReturnToDraft()
  const scheduled = module?.marks.status === 'scheduled'
  const description = !module
    ? ''
    : scheduled && module.marks.publishedAt
      ? `Students have not seen these marks. ${module.code} will be removed from the publication scheduled for ${formatDateTime(module.marks.publishedAt, timeZone)}.`
      : 'The lecturers can edit marks again and must resubmit.'

  return (
    <ReasonDialog
      open={module !== null}
      onOpenChange={onOpenChange}
      title={module ? `Return ${module.code} to draft?` : ''}
      description={description}
      confirmLabel="Return to draft"
      reasonHint="The lecturers see why in the audit trail. Recorded in the audit log."
      errorContext={module ? { code: module.code, audience: 'admin' } : { audience: 'admin' }}
      onConfirm={async (reason) => {
        if (!module) return
        const result = await returnToDraft.mutateAsync({
          code: module.code,
          body: { reason, academicYear },
        })
        toast.success(
          result.fromScheduledPublication
            ? `${module.code} is back in draft and out of the scheduled publication.`
            : `${module.code} is back in draft. Its lecturers can edit marks again.`,
        )
      }}
    />
  )
}
