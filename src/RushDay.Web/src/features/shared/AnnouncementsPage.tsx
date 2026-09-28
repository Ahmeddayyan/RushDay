import { Megaphone } from 'lucide-react'

import { EmptyState, PageHeader } from '@/components/ui'
import { useDocumentTitle } from '@/lib/useDocumentTitle'

/** Placeholder for stage S3; the real feed against GET /api/announcements is stage S5's job. */
export function Component() {
  useDocumentTitle('Announcements · RushDay')
  return (
    <div className="mx-auto max-w-3xl px-4 py-10">
      <PageHeader title="Announcements" description="Notices from your modules and the university." />
      <EmptyState icon={Megaphone} title="Nothing announced yet" />
    </div>
  )
}
