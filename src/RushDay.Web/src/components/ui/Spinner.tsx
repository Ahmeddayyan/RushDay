import { LoaderCircle } from 'lucide-react'

import { cn } from '@/lib/cn'

export type SpinnerSize = 'sm' | 'md' | 'lg'

const sizes: Record<SpinnerSize, string> = {
  sm: 'size-4',
  md: 'size-6',
  lg: 'size-8',
}

export interface SpinnerProps {
  size?: SpinnerSize
  /** Announced to assistive technology; keep it specific ("Loading modules"). */
  label?: string
  className?: string
}

export function Spinner({ size = 'md', label = 'Loading', className }: SpinnerProps) {
  return (
    <span role="status" className={cn('inline-flex items-center text-muted', className)}>
      <LoaderCircle aria-hidden="true" className={cn('animate-spin', sizes[size])} />
      <span className="sr-only">{label}</span>
    </span>
  )
}
