import { usePublicStatus } from '@/api/endpoints/public'
import type { SupportInfo } from '@/api/types/public'

export interface SupportLinkProps {
  /** Start with a capital letter ("Contact the academic office …") at the start of a sentence. */
  capitalise?: boolean
  /** Overrides the institution's support details from `['public','status']`. */
  support?: SupportInfo | null
}

/**
 * Wherever copy says "contact the academic office" (05-frontend.md section 10): with the support
 * email as a mailto link or the help URL as a link when the institution has set one, plain text
 * otherwise. `lib/format.ts` `supportText` is the plain-text twin used inside `describeProblem`.
 */
export function SupportLink({ capitalise = false, support }: SupportLinkProps) {
  const { data } = usePublicStatus()
  const details = support !== undefined ? support : data?.institution.support
  const lead = capitalise ? 'Contact the academic office' : 'contact the academic office'
  const linkClass = 'font-medium text-primary underline underline-offset-2 hover:no-underline'

  if (details?.email) {
    return (
      <>
        {lead} at{' '}
        <a href={`mailto:${details.email}`} className={linkClass}>
          {details.email}
        </a>
      </>
    )
  }
  if (details?.url) {
    return (
      <>
        {lead} (
        <a href={details.url} className={linkClass} target="_blank" rel="noopener noreferrer">
          {details.url.replace(/^https?:\/\//, '')}
          <span className="sr-only"> (opens in a new tab)</span>
        </a>
        )
      </>
    )
  }
  return <>{lead}</>
}
