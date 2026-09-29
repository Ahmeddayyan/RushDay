import type { ReactNode } from 'react'
import { Inbox, type LucideIcon } from 'lucide-react'

import { cn } from '@/lib/cn'

export interface EmptyStateProps {
  /** One sentence naming what would fill this space ("No announcements yet."). */
  title: string
  /** One sentence of explanation. */
  description?: ReactNode
  icon?: LucideIcon
  /** One action, usually a <Button> or <ButtonLink> ("Browse modules"). */
  action?: ReactNode
  /** Tighter spacing inside a card. */
  compact?: boolean
  headingLevel?: 'h1' | 'h2' | 'h3' | 'p'
  className?: string
}

/** Icon (hidden from assistive technology), one-sentence heading, one sentence, one action. */
export function EmptyState({
  title,
  description,
  icon: Icon = Inbox,
  action,
  compact = false,
  headingLevel: Heading = 'h2',
  className,
}: EmptyStateProps) {
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
          'flex items-center justify-center rounded-full bg-surface-2 text-muted ring-1 ring-border',
          compact ? 'size-10' : 'size-12',
        )}
      >
        <Icon className={compact ? 'size-5' : 'size-6'} />
      </span>
      <Heading className={cn('font-semibold text-text', compact ? 'text-sm' : 'text-base')}>
        {title}
      </Heading>
      {description && <div className="max-w-prose text-sm text-muted">{description}</div>}
      {action && <div className="mt-1 flex flex-wrap justify-center gap-2">{action}</div>}
    </div>
  )
}
