import { createContext, useContext } from 'react'

import { cn } from '@/lib/cn'

/**
 * The wiring <FormField> hands to the control inside it (05-frontend.md section 9.3): the id its
 * `<label for>` points at, the ids of the hint and error for `aria-describedby`, and the invalid and
 * required state. Controls (Input, PasswordInput, Textarea, Select, Combobox) read it through
 * `useFieldControl`, so a form writes `<FormField label="…" error={…}><Input {...register('x')} /></FormField>`.
 */
export interface FieldContextValue {
  id: string
  describedBy: string | undefined
  invalid: boolean
  required: boolean
}

export const FieldContext = createContext<FieldContextValue | null>(null)

export interface FieldControlProps {
  id?: string | undefined
  'aria-describedby'?: string | undefined
  'aria-invalid'?: boolean | 'true' | 'false' | 'grammar' | 'spelling' | undefined
  'aria-required'?: boolean | 'true' | 'false' | undefined
}

/** Merges the surrounding FormField's wiring with a control's own props (the field's id wins). */
export function useFieldControl(props: FieldControlProps): {
  id: string | undefined
  'aria-describedby': string | undefined
  'aria-invalid': true | undefined
  'aria-required': true | undefined
} {
  const field = useContext(FieldContext)
  const describedBy =
    [field?.describedBy, props['aria-describedby']].filter(Boolean).join(' ') || undefined
  const invalid =
    field?.invalid || props['aria-invalid'] === true || props['aria-invalid'] === 'true'
  const required =
    field?.required || props['aria-required'] === true || props['aria-required'] === 'true'
  return {
    id: field?.id ?? props.id,
    'aria-describedby': describedBy,
    'aria-invalid': invalid ? true : undefined,
    'aria-required': required ? true : undefined,
  }
}

/**
 * Text-control look. The border uses `--subtle` (5:1 on the surface) rather than `--border` so the
 * field's edge meets the 3:1 non-text contrast of WCAG 1.4.11 in both themes.
 */
export function controlClassName(...extra: (string | false | null | undefined)[]): string {
  return cn(
    'w-full min-w-0 rounded-md border border-subtle bg-surface px-3 text-base text-text',
    'placeholder:text-subtle',
    'focus-visible:border-focus focus-visible:outline-2 focus-visible:outline-offset-0 focus-visible:outline-focus',
    'aria-invalid:border-danger aria-invalid:focus-visible:outline-danger',
    'disabled:cursor-not-allowed disabled:border-border disabled:bg-surface-2 disabled:text-muted',
    'read-only:bg-surface-2',
    ...extra,
  )
}
