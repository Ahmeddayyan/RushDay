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
  /** A spinner inside something that already announces its busy state (a loading button). */
  decorative?: boolean
  className?: string
}

export function Spinner({
  size = 'md',
  label = 'Loading',
  decorative = false,
  className,
}: SpinnerProps) {
  const icon = <LoaderCircle aria-hidden="true" className={cn('animate-spin', sizes[size])} />
  if (decorative) return <span className={cn('inline-flex text-muted', className)}>{icon}</span>
  return (
    <span role="status" className={cn('inline-flex items-center text-muted', className)}>
      {icon}
      <span className="sr-only">{label}</span>
    </span>
  )
}
