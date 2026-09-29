import { cn } from '@/lib/cn'

/** Shared by <Dialog>, <AlertDialog> and the mobile navigation drawer. */
export const overlayClassName = 'fixed inset-0 z-50 animate-fade-in bg-black/50'

/** Centred panel of at most the width the caller adds; a full-screen sheet below 640 px. */
export const panelClassName = cn(
  'fixed z-50 flex flex-col overflow-y-auto border-border bg-surface text-text shadow-overlay outline-none',
  'inset-0 animate-sheet-in',
  'sm:inset-auto sm:top-1/2 sm:left-1/2 sm:max-h-[calc(100dvh-4rem)] sm:w-[calc(100vw-2rem)] sm:-translate-x-1/2 sm:-translate-y-1/2',
  'sm:animate-pop-in sm:rounded-xl sm:border',
)
