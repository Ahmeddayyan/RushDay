import { Moon, Sun } from 'lucide-react'

import { Button } from '@/components/ui/Button'

import { useTheme } from './ThemeContext'

export function ThemeToggle({ className }: { className?: string }) {
  const { theme, toggleTheme } = useTheme()
  const next = theme === 'dark' ? 'light' : 'dark'
  const label = `Switch to ${next} theme`

  return (
    <Button
      variant="ghost"
      size="icon"
      onClick={toggleTheme}
      aria-label={label}
      title={label}
      className={className}
    >
      {theme === 'dark' ? (
        <Sun aria-hidden="true" className="size-5" />
      ) : (
        <Moon aria-hidden="true" className="size-5" />
      )}
    </Button>
  )
}
