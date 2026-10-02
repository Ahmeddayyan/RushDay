import { toast } from '@/lib/toast'

import { useTrimModule } from '../hooks/useModules'

import { ReasonDialog } from './ReasonDialog'

/**
 * "Trim to capacity" (05-frontend.md section 10): withdraws the most recent enrolments of an
 * over-capacity module until it is back at capacity, each recorded in the audit log.
 */
export function TrimToCapacityDialog({
  code,
  capacity,
  enrolledCount,
  open,
  onOpenChange,
}: {
  code: string
  capacity: number
  enrolledCount: number
  open: boolean
  onOpenChange: (open: boolean) => void
}) {
  const trim = useTrimModule(code)
  const excess = Math.max(0, enrolledCount - capacity)

  return (
    <ReasonDialog
      open={open}
      onOpenChange={onOpenChange}
      title={`Trim ${code} to capacity?`}
      description={`Withdraws the ${excess} most recent enrolments so ${code} has ${capacity} students. Each is recorded in the audit log.`}
      confirmLabel={`Withdraw ${excess} ${excess === 1 ? 'enrolment' : 'enrolments'}`}
      reasonHint="Recorded on every withdrawal."
      errorContext={{ code }}
      onConfirm={async (reason) => {
        const result = await trim.mutateAsync(reason)
        toast.success(
          `${code} trimmed from ${result.before} to ${result.after} students. ${result.withdrawn.length} withdrawn.`,
        )
      }}
    />
  )
}
