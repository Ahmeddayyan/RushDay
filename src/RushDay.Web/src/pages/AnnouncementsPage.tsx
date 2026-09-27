import { Megaphone } from 'lucide-react'

import { PageHeader } from '@/components/layout/PageHeader'
import { Card, EmptyState } from '@/components/ui'
import { usePageTitle } from '@/lib/usePageTitle'

export function AnnouncementsPage() {
  usePageTitle('Announcements')

  return (
    <>
      <PageHeader
        title="Announcements"
        description="Notices from your modules and the university."
      />
      <Card>
        <EmptyState
          icon={Megaphone}
          title="Nothing announced yet"
          description="When staff publish a notice it will show up here, newest first."
        />
      </Card>
    </>
  )
}
