import type { ReactElement, ReactNode } from 'react'
import * as RadixTooltip from '@radix-ui/react-tooltip'

import { cn } from '@/lib/cn'

/** Mounted once in app/providers.tsx. */
export const TooltipProvider = RadixTooltip.Provider

export interface TooltipProps {
  content: ReactNode
  /** One focusable element (a button or link), so keyboard users can open the tooltip too. */
  children: ReactElement
  side?: 'top' | 'right' | 'bottom' | 'left'
  className?: string
}

/**
 * Supplementary text on hover and focus (the relative form of a date, a tile's explanation). It is
 * never the only place information lives: touch screens do not hover, so anything essential is in
 * the page text too.
 */
export function Tooltip({ content, children, side = 'top', className }: TooltipProps) {
  return (
    <RadixTooltip.Root>
      <RadixTooltip.Trigger asChild>{children}</RadixTooltip.Trigger>
      <RadixTooltip.Portal>
        <RadixTooltip.Content
          side={side}
          sideOffset={6}
          collisionPadding={8}
          className={cn(
            'z-50 max-w-xs animate-fade-in rounded-md bg-text px-2.5 py-1.5 text-xs leading-snug text-background shadow-overlay',
            className,
          )}
        >
          {content}
          <RadixTooltip.Arrow className="fill-text" />
        </RadixTooltip.Content>
      </RadixTooltip.Portal>
    </RadixTooltip.Root>
  )
}
