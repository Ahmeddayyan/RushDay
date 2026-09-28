import { Moon, Sun } from 'lucide-react'

import { Button } from '@/components/ui'
import { useTheme } from '@/lib/theme'

/**
 * Header icon toggle. The full three-way System/Light/Dark radiogroup (05-frontend.md section 10,
 * `/account`) is stage S5's job; this is a working two-way toggle in the meantime.
 */
export function ThemeToggle() {
  const { resolved, setMode } = useTheme()
  const next = resolved === 'dark' ? 'light' : 'dark'
  const label = `Switch to ${next} theme`

  return (
    <Button variant="ghost" size="icon" aria-label={label} title={label} onClick={() => setMode(next)}>
      {resolved === 'dark' ? (
        <Sun aria-hidden="true" className="size-5" />
      ) : (
        <Moon aria-hidden="true" className="size-5" />
      )}
    </Button>
  )
}
