import type { ComponentPropsWithRef } from 'react'

import { controlClassName, useFieldControl } from './field'

export type InputProps = ComponentPropsWithRef<'input'>

/**
 * A text input. Inside <FormField> it takes the field's id, `aria-describedby` and invalid state;
 * it works with react-hook-form's `register()` (ref included). Numeric entry uses
 * `type="text" inputMode="numeric"` (no spinners, no wheel changes; 05-frontend.md section 12).
 */
export function Input({ className, ...props }: InputProps) {
  const field = useFieldControl(props)
  return (
    <input
      {...props}
      {...field}
      className={controlClassName('h-10 pointer-coarse:h-11', className)}
    />
  )
}
