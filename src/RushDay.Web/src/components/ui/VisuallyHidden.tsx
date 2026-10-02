import type { ComponentProps } from 'react'

import { cn } from '@/lib/cn'

/** Text for assistive technology only: announced and read, never seen. */
export function VisuallyHidden({ className, ...props }: ComponentProps<'span'>) {
  return <span className={cn('sr-only', className)} {...props} />
}
