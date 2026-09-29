import { useState } from 'react'
import * as RadixDialog from '@radix-ui/react-dialog'
import { Menu, X } from 'lucide-react'

import { Button } from '@/components/ui'
import { overlayClassName } from '@/components/ui/dialog-styles'

import type { NavItem } from './navItems'
import { NavList } from './Sidebar'
import { ThemeToggle } from './ThemeToggle'
import { Wordmark } from './Wordmark'

/**
 * Navigation below 1024 px: a menu button and a slide-in panel built on Radix Dialog, so focus is
 * trapped inside while it is open, Escape and the backdrop close it, and focus returns to the
 * button. Any link closes it.
 */
export function MobileDrawer({ items }: { items: NavItem[] }) {
  const [open, setOpen] = useState(false)

  return (
    <RadixDialog.Root open={open} onOpenChange={setOpen}>
      <RadixDialog.Trigger asChild>
        <Button
          variant="ghost"
          size="icon"
          aria-label="Open navigation"
          className="-ml-2 lg:hidden"
        >
          <Menu aria-hidden="true" className="size-5" />
        </Button>
      </RadixDialog.Trigger>
      <RadixDialog.Portal>
        <RadixDialog.Overlay className={overlayClassName} />
        <RadixDialog.Content
          aria-describedby={undefined}
          className="fixed inset-y-0 left-0 z-50 flex w-[min(20rem,85vw)] animate-drawer-in flex-col border-r border-border bg-surface shadow-overlay outline-none"
        >
          <RadixDialog.Title className="sr-only">Navigation</RadixDialog.Title>
          <div className="flex h-16 shrink-0 items-center justify-between border-b border-border pr-3 pl-5">
            <Wordmark />
            <RadixDialog.Close asChild>
              <Button variant="ghost" size="icon" aria-label="Close navigation">
                <X aria-hidden="true" className="size-5" />
              </Button>
            </RadixDialog.Close>
          </div>
          <nav aria-label="Primary" className="flex-1 overflow-y-auto px-3 py-4">
            <NavList items={items} onNavigate={() => setOpen(false)} />
          </nav>
          <div className="flex items-center justify-between gap-3 border-t border-border px-5 py-4">
            <span id="drawer-theme-label" className="text-sm font-medium text-muted">
              Theme
            </span>
            <ThemeToggle variant="compact" labelledBy="drawer-theme-label" />
          </div>
        </RadixDialog.Content>
      </RadixDialog.Portal>
    </RadixDialog.Root>
  )
}
