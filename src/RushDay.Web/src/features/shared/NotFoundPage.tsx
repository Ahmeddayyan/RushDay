import { ArrowLeft, Compass } from 'lucide-react'

import { ButtonLink, EmptyState } from '@/components/ui'
import { useDocumentTitle } from '@/lib/useDocumentTitle'

export function Component() {
  useDocumentTitle("Page not found · RushDay")
  return (
    <div className="flex min-h-dvh items-center justify-center px-4">
      <EmptyState
        icon={Compass}
        title="That page doesn't exist"
        description="Check the address, or head back to the sign-in page."
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
