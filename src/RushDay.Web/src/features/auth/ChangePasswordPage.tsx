import { useDocumentTitle } from '@/lib/useDocumentTitle'

/** Placeholder for stage S3; the real form (checklist, ?required=1 banner, ...) is stage S5's job. */
export function Component() {
  useDocumentTitle('Change password · RushDay')
  return (
    <div className="mx-auto max-w-2xl px-4 py-10">
      <h1 tabIndex={-1} className="text-2xl font-semibold text-text outline-none">
        Change password
      </h1>
      <p className="mt-2 text-sm text-muted">This page is built out in a later stage.</p>
    </div>
  )
}
