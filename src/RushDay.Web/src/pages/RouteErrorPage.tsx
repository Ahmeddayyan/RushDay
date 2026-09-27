import { isRouteErrorResponse, useRouteError } from 'react-router'

import { ButtonLink, ErrorState } from '@/components/ui'

import { NotFoundPage } from './NotFoundPage'

/** errorElement for the route tree: 404s render the not-found page, anything else a recoverable error. */
export function RouteErrorPage() {
  const error = useRouteError()

  if (isRouteErrorResponse(error) && error.status === 404) return <NotFoundPage />

  return (
    <ErrorState
      title="Something went wrong"
      error={error}
      onRetry={() => window.location.reload()}
      retryLabel="Reload the page"
      action={
        <ButtonLink to="/" variant="ghost" size="sm">
          Back to dashboard
        </ButtonLink>
      }
    />
  )
}
