import type { ComponentPropsWithRef } from 'react'
import { LoaderCircle } from 'lucide-react'

import { cn } from '@/lib/cn'

import { buttonStyles, type ButtonSize, type ButtonVariant } from './button-variants'

export interface ButtonProps extends ComponentPropsWithRef<'button'> {
  variant?: ButtonVariant
  size?: ButtonSize
  /** Shows a spinner and blocks further clicks while an action is in flight. */
  loading?: boolean
}

export function Button({
  variant = 'primary',
  size = 'md',
  loading = false,
  disabled,
  className,
  children,
  type = 'button',
  ...props
}: ButtonProps) {
  return (
    <button
      type={type}
      className={cn(buttonStyles({ variant, size }), className)}
      disabled={disabled || loading}
      aria-busy={loading || undefined}
      {...props}
    >
      {loading && <LoaderCircle aria-hidden="true" className="size-4 animate-spin" />}
      {children}
    </button>
  )
}
