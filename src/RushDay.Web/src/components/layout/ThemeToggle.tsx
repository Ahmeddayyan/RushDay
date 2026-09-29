import { useId } from 'react'
import { Monitor, Moon, Sun, type LucideIcon } from 'lucide-react'

import { cn } from '@/lib/cn'
import { useTheme, type ThemeMode } from '@/lib/theme'

const options: { mode: ThemeMode; label: string; icon: LucideIcon }[] = [
  { mode: 'system', label: 'System', icon: Monitor },
  { mode: 'light', label: 'Light', icon: Sun },
  { mode: 'dark', label: 'Dark', icon: Moon },
]

export interface ThemeToggleProps {
  /** `full` shows the words (the account page); `compact` shows icons with the words for screen readers. */
  variant?: 'full' | 'compact'
  /** Names the group; defaults to "Theme". */
  label?: string
  /** Point at a visible heading instead of `label`. */
  labelledBy?: string
  className?: string
}

/**
 * System / Light / Dark as a `role="radiogroup"` of native radios (arrow keys move between them).
 * The choice is stored in localStorage when storage works and applies either way (lib/theme.ts).
 */
export function ThemeToggle({
  variant = 'full',
  label = 'Theme',
  labelledBy,
  className,
}: ThemeToggleProps) {
  const { mode, setMode } = useTheme()
  const name = useId()
  const compact = variant === 'compact'

  return (
    <div
      role="radiogroup"
      aria-label={labelledBy ? undefined : label}
      aria-labelledby={labelledBy}
      className={cn(
        'inline-flex items-center gap-0.5 rounded-lg border border-border bg-surface-2 p-0.5',
        className,
      )}
    >
      {options.map(({ mode: option, label: text, icon: Icon }) => {
        const checked = mode === option
        return (
          <label
            key={option}
            title={compact ? `${text} theme` : undefined}
            className={cn(
              'relative inline-flex cursor-pointer items-center justify-center gap-1.5 rounded-md text-sm font-medium select-none',
              'has-[input:focus-visible]:outline-2 has-[input:focus-visible]:outline-offset-1 has-[input:focus-visible]:outline-focus',
              compact ? 'size-8 pointer-coarse:size-11' : 'h-8 px-3 pointer-coarse:h-11',
              checked
                ? 'bg-surface text-text shadow-card ring-1 ring-border'
                : 'text-muted hover:text-text',
            )}
          >
            <input
              type="radio"
              name={name}
              value={option}
              checked={checked}
              onChange={() => setMode(option)}
              className="sr-only"
            />
            <Icon aria-hidden="true" className="size-4" />
            <span className={cn(compact && 'sr-only')}>{text}</span>
          </label>
        )
      })}
    </div>
  )
}
