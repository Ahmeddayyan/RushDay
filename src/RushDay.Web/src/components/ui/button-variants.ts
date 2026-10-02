import { cn } from '@/lib/cn'

/**
 * Button look shared by <Button> and <ButtonLink> (05-frontend.md section 9.3): four variants,
 * heights 36/40/44, and at least 44 px on coarse pointers (touch screens) whatever the size.
 */
export type ButtonVariant = 'primary' | 'secondary' | 'ghost' | 'danger'
export type ButtonSize = 'sm' | 'md' | 'lg' | 'icon' | 'icon-sm'

const base =
  'relative inline-flex shrink-0 items-center justify-center gap-2 rounded-md font-medium whitespace-nowrap ' +
  'transition-colors select-none cursor-pointer ' +
  'disabled:cursor-not-allowed disabled:opacity-55 aria-disabled:cursor-not-allowed aria-disabled:opacity-55'

const variants: Record<ButtonVariant, string> = {
  primary:
    'bg-primary text-primary-foreground shadow-card hover:bg-primary-hover disabled:hover:bg-primary',
  secondary:
    'border border-border-strong bg-surface text-text shadow-card hover:bg-surface-2 disabled:hover:bg-surface',
  ghost: 'text-text hover:bg-surface-2 disabled:hover:bg-transparent',
  // --primary-foreground is "ink on a strong colour" in both themes, so it also reads on --danger (6.5:1).
  danger: 'bg-danger text-primary-foreground shadow-card hover:opacity-90',
}

const sizes: Record<ButtonSize, string> = {
  sm: 'h-9 px-3 text-sm pointer-coarse:h-11',
  md: 'h-10 px-4 text-sm pointer-coarse:h-11',
  lg: 'h-11 px-5 text-base',
  icon: 'size-10 pointer-coarse:size-11',
  'icon-sm': 'size-9 pointer-coarse:size-11',
}

export interface ButtonStyleOptions {
  variant?: ButtonVariant
  size?: ButtonSize
}

export function buttonStyles({
  variant = 'primary',
  size = 'md',
}: ButtonStyleOptions = {}): string {
  return cn(base, variants[variant], sizes[size])
}
