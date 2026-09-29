import { LogIn } from 'lucide-react'

import { useAuth } from '@/app/AuthProvider'
import { ButtonLink } from '@/components/ui'
import { cn } from '@/lib/cn'

import { MobileDrawer } from './MobileDrawer'
import type { NavItem } from './navItems'
import { ThemeToggle } from './ThemeToggle'
import { UserMenu } from './UserMenu'
import { Wordmark } from './Wordmark'

export interface TopBarProps {
  items: NavItem[]
  /** False while a gate is in force or for visitors: no drawer, the wordmark shows at every width. */
  navigation: boolean
  gated?: boolean
}

export function TopBar({ items, navigation, gated = false }: TopBarProps) {
  const { status, user } = useAuth()
  const signedIn = status === 'authenticated' && user !== null

  return (
    <header className="sticky top-0 z-30 border-b border-border bg-surface/95 backdrop-blur supports-[backdrop-filter]:bg-surface/85">
      <div className="flex h-16 items-center gap-2 px-4 md:px-6 xl:px-8">
        {navigation && <MobileDrawer items={items} />}
        <Wordmark to="/" className={cn(navigation && 'lg:hidden')} />
        <div className="ml-auto flex items-center gap-2 sm:gap-3">
          <ThemeToggle variant="compact" className={cn(navigation && 'max-sm:hidden')} />
          {signedIn ? (
            <UserMenu gated={gated} />
          ) : status === 'booting' ? null : (
            <ButtonLink to="/login" variant="secondary" size="sm">
              <LogIn aria-hidden="true" className="size-4" />
              Sign in
            </ButtonLink>
          )}
        </div>
      </div>
    </header>
  )
}
