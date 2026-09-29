import { useState, type ComponentPropsWithRef } from 'react'
import { Eye, EyeOff } from 'lucide-react'

import { cn } from '@/lib/cn'

import { controlClassName, useFieldControl } from './field'

export interface PasswordInputProps extends Omit<ComponentPropsWithRef<'input'>, 'type'> {
  /** `current-password` on sign-in, `new-password` when choosing one (05-frontend.md section 12). */
  autoComplete: 'current-password' | 'new-password'
}

/** A password field with a show/hide toggle (`aria-pressed`), so people can check what they typed. */
export function PasswordInput({ className, ...props }: PasswordInputProps) {
  const [visible, setVisible] = useState(false)
  const field = useFieldControl(props)

  return (
    <div className="relative">
      <input
        {...props}
        {...field}
        type={visible ? 'text' : 'password'}
        autoCapitalize="none"
        autoCorrect="off"
        spellCheck={false}
        className={controlClassName('h-10 pr-12 pointer-coarse:h-11', className)}
      />
      <button
        type="button"
        aria-pressed={visible}
        aria-label="Show password"
        aria-controls={field.id}
        onClick={() => setVisible((value) => !value)}
        disabled={props.disabled}
        className={cn(
          'absolute inset-y-0 right-0 flex w-11 cursor-pointer items-center justify-center rounded-r-md text-muted',
          'hover:text-text focus-visible:outline-offset-[-2px] disabled:cursor-not-allowed',
        )}
      >
        {visible ? (
          <EyeOff aria-hidden="true" className="size-4" />
        ) : (
          <Eye aria-hidden="true" className="size-4" />
        )}
      </button>
    </div>
  )
}
