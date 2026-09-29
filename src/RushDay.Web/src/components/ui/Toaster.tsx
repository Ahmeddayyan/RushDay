import { Toaster as Sonner } from 'sonner'

import { useTheme } from '@/lib/theme'

/**
 * The single toast outlet (mounted in app/providers.tsx). Toasts are raised through `lib/toast.ts`,
 * which gives success and info messages `role="status"` and errors `role="alert"`. Unstyled, so the
 * look comes from the RushDay tokens in both themes; every toast has a close button.
 */
export function Toaster() {
  const { resolved } = useTheme()
  return (
    <Sonner
      theme={resolved}
      position="bottom-right"
      closeButton
      duration={6000}
      offset={16}
      mobileOffset={{ bottom: 88, left: 16, right: 16 }}
      containerAriaLabel="Notifications"
      toastOptions={{
        unstyled: true,
        classNames: {
          toast:
            'group flex w-(--width) items-start gap-3 rounded-lg border border-border bg-surface py-3.5 pr-10 pl-4 text-sm text-text shadow-overlay',
          title: 'font-medium leading-snug',
          description: 'mt-0.5 text-muted',
          content: 'min-w-0 flex-1',
          icon: 'mt-0.5 flex size-4 shrink-0 items-center [&>svg]:size-4',
          success: '[&_[data-icon]]:text-success',
          info: '[&_[data-icon]]:text-info',
          warning: '[&_[data-icon]]:text-warning',
          error: 'border-danger/40 [&_[data-icon]]:text-danger',
          closeButton:
            'absolute top-2.5 right-2.5 flex size-7 cursor-pointer items-center justify-center rounded-md text-muted hover:bg-surface-2 hover:text-text [&>svg]:size-3.5',
          actionButton:
            'ml-2 rounded-md bg-primary px-2.5 py-1 text-xs font-medium text-primary-foreground hover:bg-primary-hover',
        },
      }}
    />
  )
}
