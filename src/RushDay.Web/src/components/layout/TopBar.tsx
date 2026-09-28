import { MobileDrawer } from './MobileDrawer'
import { ThemeToggle } from './ThemeToggle'
import { UserMenu } from './UserMenu'

export function TopBar() {
  return (
    <header className="sticky top-0 z-30 border-b border-border bg-surface/95 backdrop-blur">
      <div className="flex h-14 items-center gap-2 px-3 sm:px-6 lg:px-8">
        <MobileDrawer />
        <span className="text-lg font-semibold tracking-tight text-text lg:hidden">RushDay</span>
        <div className="ml-auto flex items-center gap-1 sm:gap-2">
          <ThemeToggle />
          <UserMenu />
        </div>
      </div>
    </header>
  )
}
