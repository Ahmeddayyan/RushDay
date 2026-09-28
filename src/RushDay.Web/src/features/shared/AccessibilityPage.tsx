import { PageHeader } from '@/components/ui'
import { useDocumentTitle } from '@/lib/useDocumentTitle'

/** Placeholder for stage S3; the full statement is stage S5's job. */
export function Component() {
  useDocumentTitle('Accessibility · RushDay')
  return (
    <div className="mx-auto max-w-2xl px-4 py-10">
      <PageHeader
        title="Accessibility"
        description="RushDay targets WCAG 2.2 AA. The full statement is added in a later stage."
      />
    </div>
  )
}
