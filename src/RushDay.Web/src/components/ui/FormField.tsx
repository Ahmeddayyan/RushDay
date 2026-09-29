import { useId, useLayoutEffect, useMemo, useRef, type ReactNode } from 'react'
import { CircleAlert } from 'lucide-react'

import { cn } from '@/lib/cn'

import { FieldContext, type FieldContextValue } from './field'

export interface FormFieldProps {
  label: ReactNode
  /** Shown under the control and linked through `aria-describedby`. */
  hint?: ReactNode
  /** The field's validation message; sets `aria-invalid` on the control. */
  error?: string | undefined
  /** Adds "required" as text to the label (never colour or an asterisk alone). */
  required?: boolean
  /** Keeps the label for assistive technology only (a search box with a visible heading). */
  hideLabel?: boolean
  /** Forces the control id; otherwise one is generated. */
  id?: string
  /** A link or button on the label row, for example "Use demo account". */
  labelAction?: ReactNode
  className?: string
  children: ReactNode
}

/**
 * Label, hint and error around one control (05-frontend.md section 9.3). The error of the first
 * invalid field in a form carries `role="alert"`, so a failed submit announces one message rather
 * than every error at once; each control still references its own error through
 * `aria-describedby`, so moving to a field reads its message.
 */
export function FormField({
  label,
  hint,
  error,
  required = false,
  hideLabel = false,
  id,
  labelAction,
  className,
  children,
}: FormFieldProps) {
  const autoId = useId()
  const controlId = id ?? `field-${autoId}`
  const hintId = hint ? `${controlId}-hint` : undefined
  const errorId = error ? `${controlId}-error` : undefined
  const errorRef = useRef<HTMLParagraphElement>(null)

  const context = useMemo<FieldContextValue>(
    () => ({
      id: controlId,
      describedBy: [hintId, errorId].filter(Boolean).join(' ') || undefined,
      invalid: Boolean(error),
      required,
    }),
    [controlId, hintId, errorId, error, required],
  )

  // role="alert" only on the first error in the surrounding form, decided before the browser (and a
  // screen reader) sees the new DOM.
  useLayoutEffect(() => {
    const node = errorRef.current
    if (!node) return
    const scope = node.closest('form') ?? document.body
    const first = scope.querySelector('[data-field-error]')
    if (first === node) node.setAttribute('role', 'alert')
    else node.removeAttribute('role')
  })

  return (
    <div className={cn('flex min-w-0 flex-col gap-1.5', className)}>
      <div
        className={cn(
          'flex items-baseline justify-between gap-3',
          hideLabel && !labelAction && 'contents',
        )}
      >
        <label
          htmlFor={controlId}
          className={cn('text-sm font-medium text-text', hideLabel && 'sr-only')}
        >
          {label}
          {required && (
            <>
              {' '}
              <span className="ml-0.5 text-xs font-normal text-muted">(required)</span>
            </>
          )}
        </label>
        {labelAction}
      </div>
      <FieldContext value={context}>{children}</FieldContext>
      {hint && (
        <div id={hintId} className="text-sm text-muted">
          {hint}
        </div>
      )}
      {error && (
        <p
          ref={errorRef}
          id={errorId}
          data-field-error=""
          className="flex items-start gap-1.5 text-sm font-medium text-danger"
        >
          <CircleAlert aria-hidden="true" className="mt-0.5 size-4 shrink-0" />
          <span>{error}</span>
        </p>
      )}
    </div>
  )
}
