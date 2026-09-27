import { ArrowLeft, Compass } from 'lucide-react'

import { ButtonLink, EmptyState } from '@/components/ui'
import { usePageTitle } from '@/lib/usePageTitle'

export function NotFoundPage() {
  usePageTitle('Page not found')

  return (
    <EmptyState
      icon={Compass}
      title="That page does not exist"
      description="Check the address, or head back to your dashboard."
      action={
        <ButtonLink to="/" variant="secondary">
          <ArrowLeft aria-hidden="true" className="size-4" />
          Back to dashboard
        </ButtonLink>
      }
    />
  )
}
