import type { ReactNode } from 'react'

import { cn } from '@/lib/cn'

export interface PageHeaderProps {
  title: string
  description?: ReactNode
  /** A small line above the title (a module code, "Administration"). */
  eyebrow?: ReactNode
  /** The page's primary action(s), right-aligned on wide screens and stacked underneath on narrow ones. */
  actions?: ReactNode
  /** Extra metadata under the description (badges, a student number). */
  children?: ReactNode
  className?: string
}

/**
 * The page heading (05-frontend.md section 10 conventions): the one `<h1 tabIndex={-1}>` of the page,
 * which the shell focuses after each navigation. `xl` on phones, `2xl` from 768 px.
 */
export function PageHeader({
  title,
  description,
  eyebrow,
  actions,
  children,
  className,
}: PageHeaderProps) {
  return (
    <div
      className={cn(
        'mb-6 flex flex-col gap-4 md:mb-8 md:flex-row md:items-end md:justify-between',
        className,
      )}
    >
      <div className="min-w-0 space-y-1.5">
        {eyebrow && <div className="text-sm font-medium text-muted">{eyebrow}</div>}
        <h1
          tabIndex={-1}
          className="text-xl font-semibold tracking-tight text-text outline-none md:text-2xl"
        >
          {title}
        </h1>
        {description && (
          <div className="max-w-prose text-sm text-muted md:text-base">{description}</div>
        )}
        {children}
      </div>
      {actions && <div className="flex shrink-0 flex-wrap gap-2">{actions}</div>}
    </div>
  )
}
