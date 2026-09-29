import type { ComponentProps, ReactNode } from 'react'
import * as RadixAlertDialog from '@radix-ui/react-alert-dialog'

import { cn } from '@/lib/cn'

import { buttonStyles, type ButtonVariant } from './button-variants'
import { overlayClassName, panelClassName } from './dialog-styles'

/**
 * Confirmation for destructive or irreversible actions (05-frontend.md section 9.3). The description
 * states the consequence ("Your place is released immediately."). Focus starts on Cancel; Escape
 * cancels; clicking outside does nothing.
 *
 * `AlertDialogAction` closes the dialog as it runs its handler. For an async action whose outcome
 * the dialog should wait for, render a <Button loading> in the footer instead and control `open`.
 */
export const AlertDialog = RadixAlertDialog.Root
export const AlertDialogTrigger = RadixAlertDialog.Trigger

export interface AlertDialogContentProps extends Omit<
  ComponentProps<typeof RadixAlertDialog.Content>,
  'title'
> {
  title: ReactNode
  /** The consequence, in one or two sentences. */
  description: ReactNode
}

export function AlertDialogContent({
  title,
  description,
  className,
  children,
  ...props
}: AlertDialogContentProps) {
  return (
    <RadixAlertDialog.Portal>
      <RadixAlertDialog.Overlay className={overlayClassName} />
      <RadixAlertDialog.Content
        className={cn(panelClassName, 'sm:max-w-[480px]', className)}
        {...props}
      >
        <div className="space-y-2 px-5 pt-5 sm:px-6 sm:pt-6">
          <RadixAlertDialog.Title className="text-lg leading-snug font-semibold tracking-tight">
            {title}
          </RadixAlertDialog.Title>
          <RadixAlertDialog.Description asChild>
            <div className="space-y-2 text-sm text-muted">{description}</div>
          </RadixAlertDialog.Description>
        </div>
        <div className="flex-1 px-5 pt-2 pb-5 sm:px-6 sm:pb-6">{children}</div>
      </RadixAlertDialog.Content>
    </RadixAlertDialog.Portal>
  )
}

export function AlertDialogFooter({ className, ...props }: ComponentProps<'div'>) {
  return (
    <div
      className={cn(
        'mt-4 flex flex-col-reverse gap-2 sm:flex-row sm:justify-end [&>*]:w-full sm:[&>*]:w-auto',
        className,
      )}
      {...props}
    />
  )
}

export function AlertDialogCancel({
  className,
  children = 'Cancel',
  ...props
}: ComponentProps<typeof RadixAlertDialog.Cancel>) {
  return (
    <RadixAlertDialog.Cancel
      className={cn(buttonStyles({ variant: 'secondary' }), className)}
      {...props}
    >
      {children}
    </RadixAlertDialog.Cancel>
  )
}

export interface AlertDialogActionProps extends ComponentProps<typeof RadixAlertDialog.Action> {
  variant?: ButtonVariant
}

export function AlertDialogAction({
  variant = 'danger',
  className,
  ...props
}: AlertDialogActionProps) {
  return <RadixAlertDialog.Action className={cn(buttonStyles({ variant }), className)} {...props} />
}
