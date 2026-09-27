import { useCallback, useEffect, useRef, useState } from 'react'
import { createPortal } from 'react-dom'
import { Menu, X } from 'lucide-react'

import { Button } from '@/components/ui/Button'

import { NavList } from './NavList'

const panelId = 'mobile-navigation'

/** Menu button plus slide-in navigation drawer for small screens. Escape, backdrop and any link close it. */
export function MobileNav() {
  const [open, setOpen] = useState(false)
  const triggerRef = useRef<HTMLButtonElement>(null)
  const panelRef = useRef<HTMLDivElement>(null)

  const close = useCallback(() => {
    setOpen(false)
    triggerRef.current?.focus()
  }, [])

  useEffect(() => {
    if (!open) return
    const onKeyDown = (event: KeyboardEvent) => {
      if (event.key === 'Escape') close()
    }
    document.addEventListener('keydown', onKeyDown)
    panelRef.current?.querySelector<HTMLElement>('nav a')?.focus()
    const previousOverflow = document.body.style.overflow
    document.body.style.overflow = 'hidden'
    return () => {
      document.removeEventListener('keydown', onKeyDown)
      document.body.style.overflow = previousOverflow
    }
  }, [open, close])

  return (
    <>
      <Button
        ref={triggerRef}
        variant="ghost"
        size="icon"
        className="md:hidden"
        aria-label="Open navigation"
        aria-expanded={open}
        aria-controls={panelId}
        onClick={() => setOpen(true)}
      >
        <Menu aria-hidden="true" className="size-5" />
      </Button>

      {/* Portalled to <body>: the sticky header's backdrop-filter would otherwise trap the fixed overlay inside it. */}
      {open &&
        createPortal(
          <div className="fixed inset-0 z-40 md:hidden">
            <button
              type="button"
              tabIndex={-1}
              aria-label="Close navigation"
              className="absolute inset-0 bg-black/40"
              onClick={close}
            />
            <div
              ref={panelRef}
              id={panelId}
              role="dialog"
              aria-modal="true"
              aria-label="Navigation"
              className="absolute inset-y-0 left-0 flex w-72 max-w-[85vw] flex-col bg-surface shadow-xl"
            >
              <div className="flex h-14 items-center justify-between border-b border-border pr-2 pl-5">
                <span className="text-lg font-semibold tracking-tight">RushDay</span>
                <Button variant="ghost" size="icon" aria-label="Close navigation" onClick={close}>
                  <X aria-hidden="true" className="size-5" />
                </Button>
              </div>
              <nav aria-label="Primary" className="flex-1 overflow-y-auto p-3">
                <NavList onNavigate={close} />
              </nav>
            </div>
          </div>,
          document.body,
        )}
    </>
  )
}
