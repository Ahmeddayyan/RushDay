import type { ComponentPropsWithRef } from 'react'
import { ChevronDown } from 'lucide-react'

import { cn } from '@/lib/cn'

import { controlClassName, useFieldControl } from './field'

export interface SelectOption {
  value: string
  label: string
  disabled?: boolean
}

export interface SelectProps extends ComponentPropsWithRef<'select'> {
  /** Shorthand for simple lists; pass <option> children instead for groups. */
  options?: readonly SelectOption[]
}

/** A native <select> (best keyboard and screen-reader support there is), styled like the text controls. */
export function Select({ className, options, children, ...props }: SelectProps) {
  const field = useFieldControl(props)
  return (
    <div className={cn('relative', className)}>
      <select
        {...props}
        {...field}
        className={controlClassName('h-10 cursor-pointer appearance-none pr-9 pointer-coarse:h-11')}
      >
        {options?.map((option) => (
          <option key={option.value} value={option.value} disabled={option.disabled}>
            {option.label}
          </option>
        ))}
        {children}
      </select>
      <ChevronDown
        aria-hidden="true"
        className="pointer-events-none absolute top-1/2 right-3 size-4 -translate-y-1/2 text-muted"
      />
    </div>
  )
}
