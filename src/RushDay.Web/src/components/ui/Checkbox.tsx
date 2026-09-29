import { useId, type ComponentPropsWithRef, type ReactNode } from 'react'

import { cn } from '@/lib/cn'

export interface CheckboxProps extends Omit<ComponentPropsWithRef<'input'>, 'type'> {
  label: ReactNode
  hint?: ReactNode
}

/**
 * A native checkbox with its label to the right and an optional hint underneath. The whole row is
 * the click target (at least 24 px, 44 px on touch screens).
 */
export function Checkbox({ label, hint, id, className, ...props }: CheckboxProps) {
  const autoId = useId()
  const inputId = id ?? `checkbox-${autoId}`
  const hintId = hint ? `${inputId}-hint` : undefined

  return (
    <div className={cn('flex items-start gap-3', className)}>
      <span className="flex h-6 items-center pointer-coarse:h-11">
        <input
          {...props}
          id={inputId}
          type="checkbox"
          aria-describedby={
            [hintId, props['aria-describedby']].filter(Boolean).join(' ') || undefined
          }
          className="size-4.5 cursor-pointer rounded-sm border-subtle accent-primary disabled:cursor-not-allowed"
        />
      </span>
      <span className="flex min-w-0 flex-col pt-0.5 pointer-coarse:pt-2.5">
        <label htmlFor={inputId} className="cursor-pointer text-sm font-medium text-text">
          {label}
        </label>
        {hint && (
          <span id={hintId} className="text-sm text-muted">
            {hint}
          </span>
        )}
      </span>
    </div>
  )
}
