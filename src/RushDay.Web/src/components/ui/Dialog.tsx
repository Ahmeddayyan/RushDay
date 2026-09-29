import type { ComponentProps, ReactNode } from 'react'
import * as RadixDialog from '@radix-ui/react-dialog'
import { X } from 'lucide-react'

import { cn } from '@/lib/cn'

import { Button } from './Button'
import { overlayClassName, panelClassName } from './dialog-styles'

/**
 * Radix Dialog (05-frontend.md section 9.3): focus trap, Escape, focus restored to the trigger;
 * at most 480 px wide, a full-screen sheet below 640 px. Destructive or irreversible confirmations
 * use <AlertDialog> instead.
 */
export const Dialog = RadixDialog.Root
export const DialogTrigger = RadixDialog.Trigger
export const DialogClose = RadixDialog.Close

export interface DialogContentProps extends Omit<
  ComponentProps<typeof RadixDialog.Content>,
  'title'
> {
  title: ReactNode
  description?: ReactNode
  /** Wider panel for forms with two columns; still a sheet below 640 px. */
  size?: 'default' | 'wide'
  hideClose?: boolean
}

export function DialogContent({
  title,
  description,
  size = 'default',
  hideClose = false,
  className,
  children,
  ...props
}: DialogContentProps) {
  return (
    <RadixDialog.Portal>
      <RadixDialog.Overlay className={overlayClassName} />
      <RadixDialog.Content
        className={cn(
          panelClassName,
          size === 'wide' ? 'sm:max-w-2xl' : 'sm:max-w-[480px]',
          className,
        )}
        {...(description ? {} : { 'aria-describedby': undefined })}
        {...props}
      >
        <div className="flex items-start justify-between gap-4 px-5 pt-5 sm:px-6 sm:pt-6">
          <div className="min-w-0 space-y-1.5">
            <RadixDialog.Title className="text-lg leading-snug font-semibold tracking-tight">
              {title}
            </RadixDialog.Title>
            {description && (
              <RadixDialog.Description className="text-sm text-muted">
                {description}
              </RadixDialog.Description>
            )}
          </div>
          {!hideClose && (
            <RadixDialog.Close asChild>
              <Button variant="ghost" size="icon-sm" aria-label="Close" className="-mt-1 -mr-2">
                <X aria-hidden="true" className="size-4" />
              </Button>
            </RadixDialog.Close>
          )}
        </div>
        <div className="flex-1 px-5 py-4 sm:px-6">{children}</div>
      </RadixDialog.Content>
    </RadixDialog.Portal>
  )
}

/** Buttons at the bottom of a dialog: primary last, stacked full-width on phones. */
export function DialogFooter({ className, ...props }: ComponentProps<'div'>) {
  return (
    <div
      className={cn(
        'mt-6 flex flex-col-reverse gap-2 sm:flex-row sm:flex-wrap sm:justify-end [&>*]:w-full sm:[&>*]:w-auto',
        className,
      )}
      {...props}
    />
  )
}
