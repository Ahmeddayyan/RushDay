import { Settings } from 'lucide-react'

import { PageHeader } from '@/components/layout/PageHeader'
import { Badge, Card, EmptyState } from '@/components/ui'
import { usePageTitle } from '@/lib/usePageTitle'

export function AdminPage() {
  usePageTitle('Admin')

  return (
    <>
      <PageHeader
        title="Admin"
        description="Users, roles and the results-day publish switch."
        actions={<Badge variant="warning">Admins only</Badge>}
      />
      <Card>
        <EmptyState
          icon={Settings}
          title="Administration is coming"
          description="Portal administration lives here once the spec defines it."
        />
      </Card>
    </>
  )
}
