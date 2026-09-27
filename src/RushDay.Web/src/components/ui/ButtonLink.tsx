import type { ComponentPropsWithRef } from 'react'
import { Link } from 'react-router'

import { cn } from '@/lib/cn'

import { buttonStyles, type ButtonSize, type ButtonVariant } from './button-variants'

export interface ButtonLinkProps extends ComponentPropsWithRef<typeof Link> {
  variant?: ButtonVariant
  size?: ButtonSize
}

/** A router <Link> that looks like a <Button>. Use it for navigation; use <Button> for actions. */
export function ButtonLink({
  variant = 'primary',
  size = 'md',
  className,
  ...props
}: ButtonLinkProps) {
  return <Link className={cn(buttonStyles({ variant, size }), className)} {...props} />
}
