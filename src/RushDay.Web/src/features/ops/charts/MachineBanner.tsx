import { Info } from 'lucide-react'

/**
 * "Measured on {machine}. Live figures for this server are on the Operations page for
 * administrators." (05-frontend.md section 10, `/story`). Honest about where the numbers below came
 * from: one laptop, not the deployed server.
 */
export function MachineBanner({ machine }: { machine: string }) {
  return (
    <div className="flex items-start gap-3 rounded-lg border border-border bg-surface-2 p-4 text-sm text-muted">
      <Info aria-hidden="true" className="mt-0.5 size-4 shrink-0" />
      <p>
        Measured on {machine}. Live figures for this server are on the Operations page for
        administrators.
      </p>
    </div>
  )
}
