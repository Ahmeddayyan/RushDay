import { useId, type ComponentPropsWithRef } from 'react'

import { cn } from '@/lib/cn'

export interface InputProps extends Omit<ComponentPropsWithRef<'input'>, 'id'> {
  /** Always required: a field without a name is not accessible. Use hideLabel to keep it for screen readers only. */
  label: string
  hideLabel?: boolean
  hint?: string
  error?: string
  id?: string
}

/** Labelled text input with hint and error text wired up through aria-describedby. Works with react-hook-form's register(). */
export function Input({
  label,
  hideLabel = false,
  hint,
  error,
  id,
  className,
  ref,
  ...props
}: InputProps) {
  const autoId = useId()
  const inputId = id ?? autoId
  const hintId = `${inputId}-hint`
  const errorId = `${inputId}-error`
  const describedBy =
    [hint ? hintId : null, error ? errorId : null].filter(Boolean).join(' ') || undefined

  return (
    <div className="flex flex-col gap-1.5">
      <label
        htmlFor={inputId}
        className={cn('text-sm font-medium text-text', hideLabel && 'sr-only')}
      >
        {label}
      </label>
      <input
        id={inputId}
        ref={ref}
        aria-invalid={error ? true : undefined}
        aria-describedby={describedBy}
        className={cn(
          'h-10 w-full min-w-0 rounded-md border border-border bg-surface px-3 text-base text-text',
          'placeholder:text-muted/70',
          'focus-visible:border-primary focus-visible:outline-2 focus-visible:outline-offset-0 focus-visible:outline-primary',
          'disabled:cursor-not-allowed disabled:opacity-60',
          error && 'border-danger focus-visible:border-danger focus-visible:outline-danger',
          className,
        )}
        {...props}
      />
      {hint && !error && (
        <p id={hintId} className="text-sm text-muted">
          {hint}
        </p>
      )}
      {error && (
        <p id={errorId} className="text-sm text-danger">
          {error}
        </p>
      )}
    </div>
  )
}
