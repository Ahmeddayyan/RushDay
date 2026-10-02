import type { ComponentProps } from 'react'
import * as RadixMenu from '@radix-ui/react-dropdown-menu'

import { cn } from '@/lib/cn'

/**
 * Radix DropdownMenu: a `menu` with arrow-key navigation, typeahead, Escape to close and focus
 * returned to the trigger (05-frontend.md section 12). Used by the user menu and row actions.
 */
export const DropdownMenu = RadixMenu.Root
export const DropdownMenuTrigger = RadixMenu.Trigger
export const DropdownMenuGroup = RadixMenu.Group

export function DropdownMenuContent({
  className,
  sideOffset = 6,
  align = 'end',
  ...props
}: ComponentProps<typeof RadixMenu.Content>) {
  return (
    <RadixMenu.Portal>
      <RadixMenu.Content
        sideOffset={sideOffset}
        align={align}
        collisionPadding={8}
        className={cn(
          'z-50 min-w-56 animate-pop-in rounded-lg border border-border bg-surface p-1 text-text shadow-overlay',
          className,
        )}
        {...props}
      />
    </RadixMenu.Portal>
  )
}

const itemClassName =
  'relative flex min-h-9 cursor-pointer items-center gap-2.5 rounded-md px-2.5 py-1.5 text-sm outline-none select-none ' +
  'data-[highlighted]:bg-surface-2 data-[disabled]:cursor-not-allowed data-[disabled]:opacity-55 pointer-coarse:min-h-11 ' +
  '[&_svg]:size-4 [&_svg]:shrink-0 [&_svg]:text-muted'

export interface DropdownMenuItemProps extends ComponentProps<typeof RadixMenu.Item> {
  tone?: 'default' | 'danger'
}

export function DropdownMenuItem({ className, tone = 'default', ...props }: DropdownMenuItemProps) {
  return (
    <RadixMenu.Item
      className={cn(
        itemClassName,
        tone === 'danger' && 'text-danger [&_svg]:text-danger',
        className,
      )}
      {...props}
    />
  )
}

export function DropdownMenuLabel({ className, ...props }: ComponentProps<typeof RadixMenu.Label>) {
  return <RadixMenu.Label className={cn('px-2.5 py-2 text-sm', className)} {...props} />
}

export function DropdownMenuSeparator({
  className,
  ...props
}: ComponentProps<typeof RadixMenu.Separator>) {
  return <RadixMenu.Separator className={cn('-mx-1 my-1 h-px bg-border', className)} {...props} />
}
