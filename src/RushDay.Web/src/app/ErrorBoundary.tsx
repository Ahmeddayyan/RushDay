import { isRouteErrorResponse, useRouteError } from 'react-router'

/** The route tree's top-level errorElement: a page that throws keeps a minimal shell around the error. */
export function ErrorBoundary() {
  const error = useRouteError()
  const message = isRouteErrorResponse(error)
    ? `${error.status} ${error.statusText}`
    : 'Something went wrong.'

  return (
    <div role="alert" className="mx-auto max-w-md px-4 py-16 text-center">
      <h1 className="text-xl font-semibold text-text">Something went wrong</h1>
      <p className="mt-2 text-sm text-muted">{message}</p>
    </div>
  )
}
