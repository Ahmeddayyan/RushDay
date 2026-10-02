import type { ReactNode } from 'react'
import { House, ShieldAlert } from 'lucide-react'

import { cn } from '@/lib/cn'

import { ButtonLink } from './ButtonLink'

export interface ForbiddenStateProps {
  title?: string
  /** Who the page or record is for, in one sentence. */
  description?: ReactNode
  /** Defaults to a link to the signed-in person's home page. */
  action?: ReactNode
  compact?: boolean
  headingLevel?: 'h1' | 'h2' | 'h3' | 'p'
  className?: string
}

/** A 403: shown in place of the content, never a redirect loop (05-frontend.md section 6.4). */
export function ForbiddenState({
  title = "You don't have access to that page",
  description = 'This page is for a different role.',
  action,
  compact = false,
  headingLevel: Heading = 'h2',
  className,
}: ForbiddenStateProps) {
  return (
    <div
      className={cn(
        'flex flex-col items-center justify-center text-center',
        compact ? 'gap-2 px-4 py-8' : 'gap-3 px-6 py-14',
        className,
      )}
    >
      <span
        aria-hidden="true"
        className={cn(
          'flex items-center justify-center rounded-full bg-warning-soft text-warning',
          compact ? 'size-10' : 'size-12',
        )}
      >
        <ShieldAlert className={compact ? 'size-5' : 'size-6'} />
      </span>
      <Heading
        tabIndex={Heading === 'h1' ? -1 : undefined}
        className={cn(
          'font-semibold text-text outline-none',
          compact ? 'text-sm' : Heading === 'h1' ? 'text-xl md:text-2xl' : 'text-base',
        )}
      >
        {title}
      </Heading>
      {description && <div className="max-w-prose text-sm text-muted">{description}</div>}
      <div className="mt-1 flex flex-wrap justify-center gap-2">
        {action ?? (
          <ButtonLink to="/" variant="secondary">
            <House aria-hidden="true" className="size-4" />
            Go to your home page
          </ButtonLink>
        )}
      </div>
    </div>
  )
}
