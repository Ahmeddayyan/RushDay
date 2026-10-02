import type { ComponentPropsWithRef } from 'react'

import { controlClassName, useFieldControl } from './field'

export type TextareaProps = ComponentPropsWithRef<'textarea'>

/** Multi-line text (announcement bodies, reasons). Takes its wiring from the surrounding FormField. */
export function Textarea({ className, rows = 4, ...props }: TextareaProps) {
  const field = useFieldControl(props)
  return (
    <textarea
      rows={rows}
      {...props}
      {...field}
      className={controlClassName('min-h-24 py-2 leading-relaxed', className)}
    />
  )
}
