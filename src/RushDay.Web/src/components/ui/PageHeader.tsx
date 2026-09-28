import type { ReactNode } from 'react'

export interface PageHeaderProps {
  title: string
  description?: string
  /** Page-level actions, right-aligned on wide screens and stacked underneath on narrow ones. */
  actions?: ReactNode
}

export function PageHeader({ title, description, actions }: PageHeaderProps) {
  return (
    <div className="mb-6 flex flex-col gap-3 sm:flex-row sm:items-end sm:justify-between">
      <div className="min-w-0">
        <h1 tabIndex={-1} className="text-2xl font-semibold tracking-tight text-text outline-none">
          {title}
        </h1>
        {description && <p className="mt-1 text-sm text-muted">{description}</p>}
      </div>
      {actions && <div className="flex flex-wrap gap-2">{actions}</div>}
    </div>
  )
}
