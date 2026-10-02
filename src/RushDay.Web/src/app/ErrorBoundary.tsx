import { RefreshCw, TriangleAlert } from 'lucide-react'
import { isRouteErrorResponse, useRouteError } from 'react-router'

import { Wordmark } from '@/components/layout/Wordmark'
import { Button, ButtonLink } from '@/components/ui'

/**
 * A route's errorElement: when a page throws while rendering (or a lazy chunk fails to load after a
 * deploy), a calm page with a way forward replaces React Router's developer screen.
 */
export function ErrorBoundary() {
  const error = useRouteError()
  const chunkFailed =
    error instanceof Error &&
    /dynamically imported module|Importing a module script failed|error loading/i.test(
      error.message,
    )
  const message = isRouteErrorResponse(error)
    ? `The page answered ${error.status} ${error.statusText}.`
    : chunkFailed
      ? 'RushDay has been updated since this page was opened. Reload to get the new version.'
      : 'Something went wrong while showing this page.'

  return (
    <div className="flex min-h-dvh flex-col items-center justify-center gap-6 bg-background px-4 py-10">
      <Wordmark size="lg" />
      <div
        role="alert"
        className="flex w-full max-w-md flex-col items-center gap-3 rounded-lg border border-border bg-surface p-8 text-center shadow-card"
      >
        <span
          aria-hidden="true"
          className="flex size-12 items-center justify-center rounded-full bg-danger-soft text-danger"
        >
          <TriangleAlert className="size-6" />
        </span>
        <h1 className="text-xl font-semibold text-text">Something went wrong</h1>
        <p className="text-sm text-muted">{message}</p>
        <div className="mt-2 flex flex-wrap justify-center gap-2">
          <Button variant="secondary" onClick={() => window.location.reload()}>
            <RefreshCw aria-hidden="true" className="size-4" />
            Reload
          </Button>
          <ButtonLink to="/" variant="ghost">
            Go to your home page
          </ButtonLink>
        </div>
      </div>
    </div>
  )
}
