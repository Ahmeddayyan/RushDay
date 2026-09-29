import { Link } from 'react-router'

import { cn } from '@/lib/cn'

export interface WordmarkProps {
  /** Wraps the mark in a link home; omit on pages where "home" is this page. */
  to?: string
  size?: 'md' | 'lg'
  className?: string
}

/** The RushDay mark (the favicon's bolt) and name. Decorative mark, real text. */
export function Wordmark({ to, size = 'md', className }: WordmarkProps) {
  const content = (
    <>
      <svg
        aria-hidden="true"
        viewBox="0 0 32 32"
        className={cn('shrink-0', size === 'lg' ? 'size-9' : 'size-7')}
        focusable="false"
      >
        <rect width="32" height="32" rx="8" fill="var(--primary)" />
        <path d="M18 5 9 18.5h6.5L14 27l9-13.5h-6.5z" fill="var(--primary-foreground)" />
      </svg>
      <span
        className={cn(
          'font-semibold tracking-tight text-text',
          size === 'lg' ? 'text-2xl' : 'text-lg',
        )}
      >
        RushDay
      </span>
    </>
  )

  const classes = cn('inline-flex items-center gap-2.5', className)
  if (to) {
    return (
      <Link to={to} className={cn(classes, 'rounded-md')} aria-label="RushDay home">
        {content}
      </Link>
    )
  }
  return <span className={classes}>{content}</span>
}
