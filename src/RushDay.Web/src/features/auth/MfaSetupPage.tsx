import { useDocumentTitle } from '@/lib/useDocumentTitle'

/** Placeholder for stage S3; the QR code and enable flow are stage S5's job. */
export function Component() {
  useDocumentTitle('Two-step verification · RushDay')
  return (
    <div className="mx-auto max-w-2xl px-4 py-10">
      <h1 tabIndex={-1} className="text-2xl font-semibold text-text outline-none">
        Two-step verification
      </h1>
      <p className="mt-2 text-sm text-muted">This page is built out in a later stage.</p>
    </div>
  )
}
