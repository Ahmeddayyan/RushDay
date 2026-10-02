import type { ComponentPropsWithRef } from 'react'

import { cn } from '@/lib/cn'

import { buttonStyles, type ButtonSize, type ButtonVariant } from './button-variants'
import { Spinner } from './Spinner'

interface BaseButtonProps extends Omit<ComponentPropsWithRef<'button'>, 'aria-label'> {
  variant?: ButtonVariant
  /**
   * While true the button keeps its width, shows a spinner over its label, sets `aria-busy` and is
   * disabled, so a double click never sends a second request.
   */
  loading?: boolean
}

interface TextButtonProps extends BaseButtonProps {
  size?: Exclude<ButtonSize, 'icon' | 'icon-sm'>
  'aria-label'?: string
}

/** Icon-only buttons must be named: the type makes `aria-label` required. */
interface IconButtonProps extends BaseButtonProps {
  size: 'icon' | 'icon-sm'
  'aria-label': string
}

export type ButtonProps = TextButtonProps | IconButtonProps

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
      {/* The label stays in the accessibility tree (opacity, not visibility) and keeps the width. */}
      <span className={cn('inline-flex items-center justify-center gap-2', loading && 'opacity-0')}>
        {children}
      </span>
      {loading && (
        <span className="absolute inset-0 flex items-center justify-center" aria-hidden="true">
          <Spinner size="sm" decorative className="text-current" />
        </span>
      )}
    </button>
  )
}
