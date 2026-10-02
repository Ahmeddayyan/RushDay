import { useState } from 'react'
import { Link } from 'react-router'

import { usePublicStatus } from '@/api/endpoints/public'
import type { OpsDataQuality } from '@/api/types/ops'
import { describeProblem } from '@/api/problem'
import { useDemoReset, useReconcile } from '@/api/endpoints/ops'
import {
  AlertDialog,
  AlertDialogCancel,
  AlertDialogContent,
  AlertDialogFooter,
  AlertDialogTrigger,
  Button,
  Card,
  CardDescription,
  CardHeader,
  CardTitle,
} from '@/components/ui'
import { toast } from '@/lib/toast'

export interface DataQualityPanelProps {
  dataQuality: OpsDataQuality
}

function minutesAgo(instant: string): number {
  return Math.max(0, Math.round((Date.now() - Date.parse(instant)) / 60_000))
}

/**
 * Data checks (05-frontend.md section 10): modules oversold against their cached place counts, a
 * re-count action, and, in demo mode, a reset for the demo module so visitors can keep enrolling.
 */
export function DataQualityPanel({ dataQuality }: DataQualityPanelProps) {
  const status = usePublicStatus()
  const reconcile = useReconcile()
  const demoReset = useDemoReset()
  const [resetOpen, setResetOpen] = useState(false)

  async function runReconcile() {
    try {
      const result = await reconcile.mutateAsync()
      toast.success(
        result.modulesCorrected.length === 0
          ? 'No modules needed correcting.'
          : `Corrected ${result.modulesCorrected.length} module${result.modulesCorrected.length === 1 ? '' : 's'}.`,
      )
    } catch (error) {
      toast.error(describeProblem(error).message)
    }
  }

  async function runDemoReset() {
    try {
      const result = await demoReset.mutateAsync()
      toast.success(
        `Withdrew ${result.withdrawn} demo enrolment${result.withdrawn === 1 ? '' : 's'}.`,
      )
    } catch (error) {
      toast.error(describeProblem(error).message)
    } finally {
      setResetOpen(false)
    }
  }

  return (
    <Card>
      <CardHeader
        actions={
          <Button
            variant="secondary"
            size="sm"
            loading={reconcile.isPending}
            onClick={() => void runReconcile()}
          >
            Re-count places
          </Button>
        }
      >
        <CardTitle>Data checks</CardTitle>
        <CardDescription>Modules with more students than places.</CardDescription>
      </CardHeader>

      {dataQuality.modulesOverCapacity.length === 0 ? (
        <p className="text-sm text-muted">No modules are currently over capacity.</p>
      ) : (
        <ul className="list-disc space-y-1 pl-5 text-sm text-text">
          {dataQuality.modulesOverCapacity.map((module) => (
            <li key={module.code}>
              <Link to={`/admin/modules/${module.code}`} className="font-mono underline">
                {module.code}
              </Link>{' '}
              — {module.enrolledCount} enrolled of {module.capacity} places
            </li>
          ))}
        </ul>
      )}

      {dataQuality.staleSince && dataQuality.refreshedAt && (
        <p className="mt-2 text-xs text-muted">
          Checks last ran {minutesAgo(dataQuality.refreshedAt)} min ago.
        </p>
      )}

      {status.data?.demo && (
        <div className="mt-4 border-t border-border pt-4">
          <AlertDialog open={resetOpen} onOpenChange={setResetOpen}>
            <AlertDialogTrigger asChild>
              <Button variant="secondary" size="sm">
                Reset demo module
              </Button>
            </AlertDialogTrigger>
            <AlertDialogContent
              title="Reset the demo module?"
              description="Withdraws every self-service CS3099 enrolment that has no mark, so demo visitors can enrol again."
            >
              <AlertDialogFooter>
                <AlertDialogCancel />
                <Button
                  variant="danger"
                  loading={demoReset.isPending}
                  onClick={() => void runDemoReset()}
                >
                  Reset
                </Button>
              </AlertDialogFooter>
            </AlertDialogContent>
          </AlertDialog>
        </div>
      )}
    </Card>
  )
}
