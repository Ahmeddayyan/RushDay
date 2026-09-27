import { Users } from 'lucide-react'

import { PageHeader } from '@/components/layout/PageHeader'
import { Badge, Card, EmptyState } from '@/components/ui'
import { usePageTitle } from '@/lib/usePageTitle'

export function StaffPage() {
  usePageTitle('Staff')

  return (
    <>
      <PageHeader
        title="Staff"
        description="Module cohorts, mark entry and announcements."
        actions={<Badge variant="primary">Staff and admins</Badge>}
      />
      <Card>
        <EmptyState
          icon={Users}
          title="Staff tools are coming"
          description="This area is reserved for teaching staff. Its features arrive with the spec."
        />
      </Card>
    </>
  )
}
