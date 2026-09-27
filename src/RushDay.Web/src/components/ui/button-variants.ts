import { cn } from '@/lib/cn'

export type ButtonVariant = 'primary' | 'secondary' | 'ghost' | 'danger'
export type ButtonSize = 'sm' | 'md' | 'lg' | 'icon'

const base =
  'inline-flex shrink-0 items-center justify-center gap-2 rounded-md font-medium whitespace-nowrap ' +
  'transition-colors select-none ' +
  'focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-primary ' +
  'disabled:pointer-events-none disabled:opacity-60 aria-disabled:pointer-events-none aria-disabled:opacity-60'

const variants: Record<ButtonVariant, string> = {
  primary: 'bg-primary text-primary-foreground hover:bg-primary/90',
  secondary: 'border border-border bg-surface text-text hover:bg-border/40',
  ghost: 'text-text hover:bg-border/40',
  // --primary-foreground is "ink on a strong colour" in both themes, so it also reads on --danger.
  danger: 'bg-danger text-primary-foreground hover:bg-danger/90',
}

const sizes: Record<ButtonSize, string> = {
  sm: 'h-8 px-3 text-sm',
  md: 'h-10 px-4 text-sm',
  lg: 'h-11 px-5 text-base',
  icon: 'size-10',
}

export interface ButtonStyleOptions {
  variant?: ButtonVariant
  size?: ButtonSize
}

/** Shared by <Button> and <ButtonLink> so links styled as buttons stay identical. */
export function buttonStyles({
  variant = 'primary',
  size = 'md',
}: ButtonStyleOptions = {}): string {
  return cn(base, variants[variant], sizes[size])
}
