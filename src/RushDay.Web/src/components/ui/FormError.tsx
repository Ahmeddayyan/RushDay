import type { ReactNode } from 'react'
import { CircleAlert } from 'lucide-react'

import { cn } from '@/lib/cn'

export interface FormErrorProps {
  /** Nothing renders when this is empty. */
  children?: ReactNode
  title?: string
  className?: string
}

/**
 * A form-level error (a failed sign-in, a server error with no field to attach to). `role="alert"`
 * so it is announced when it appears; icon plus text, never colour alone.
 */
export function FormError({ children, title, className }: FormErrorProps) {
  if (!children) return null
  return (
    <div
      role="alert"
      className={cn(
        'flex items-start gap-2.5 rounded-md border border-danger/30 bg-danger-soft px-3.5 py-3 text-sm text-danger',
        className,
      )}
    >
      <CircleAlert aria-hidden="true" className="mt-0.5 size-4 shrink-0" />
      <div className="min-w-0 space-y-1">
        {title && <p className="font-semibold">{title}</p>}
        <div className="space-y-1 [&_a]:font-semibold [&_a]:underline">{children}</div>
      </div>
    </div>
  )
}
