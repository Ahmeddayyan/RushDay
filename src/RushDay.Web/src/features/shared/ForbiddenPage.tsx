import { ArrowLeft, ShieldAlert } from 'lucide-react'

import { ButtonLink, EmptyState } from '@/components/ui'
import { useDocumentTitle } from '@/lib/useDocumentTitle'

export function Component() {
  useDocumentTitle('Forbidden · RushDay')
  return (
    <div className="flex min-h-dvh items-center justify-center px-4">
      <EmptyState
        icon={ShieldAlert}
        title="You don't have access to that page"
        description="This area is for a different role. If you think that is wrong, contact the academic office."
        action={
          <ButtonLink to="/" variant="secondary">
            <ArrowLeft aria-hidden="true" className="size-4" />
            Back home
          </ButtonLink>
        }
      />
    </div>
  )
}
