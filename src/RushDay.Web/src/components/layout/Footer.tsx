import { Link } from 'react-router'

import { useApiIndex, usePublicStatus } from '@/api/endpoints/public'
import { SupportLink } from '@/components/ui'
import { cn } from '@/lib/cn'

export interface FooterProps {
  /**
   * The sign-in page's footer: adds the privacy note, the institution's privacy notice link and the
   * forgotten-password line (05-frontend.md section 10, `/login`).
   */
  extended?: boolean
  className?: string
}

const linkClass = 'rounded-sm underline-offset-2 hover:text-text hover:underline'

/** "Deployed {commit} · {institution} · Accessibility · About RushDay" (05-frontend.md section 7). */
export function Footer({ extended = false, className }: FooterProps) {
  const { data: index } = useApiIndex()
  const { data: status } = usePublicStatus()
  const commit = index?.commit
  const privacyUrl = status?.institution.privacyNoticeUrl

  return (
    <footer
      className={cn(
        'border-t border-border px-4 py-6 text-sm text-muted md:px-6 xl:px-8',
        className,
      )}
    >
      <div className="mx-auto flex max-w-[1200px] flex-col gap-3">
        {extended && (
          <div className="space-y-1.5">
            <p>
              Only your session cookie and your theme choice are stored. No third-party scripts.
            </p>
            <p>
              Forgotten your password? <SupportLink capitalise />.
            </p>
          </div>
        )}
        <ul className="flex flex-wrap items-center gap-x-2 gap-y-1">
          {commit && (
            <li className="after:ml-2 after:content-['·']">
              Deployed <span className="font-mono text-xs">{commit.slice(0, 7)}</span>
            </li>
          )}
          {status && <li className="after:ml-2 after:content-['·']">{status.institution.name}</li>}
          {privacyUrl && (
            <li className="after:ml-2 after:content-['·']">
              <a href={privacyUrl} className={linkClass} target="_blank" rel="noopener noreferrer">
                Privacy notice<span className="sr-only"> (opens in a new tab)</span>
              </a>
            </li>
          )}
          <li className="after:ml-2 after:content-['·']">
            <Link to="/accessibility" className={linkClass}>
              Accessibility
            </Link>
          </li>
          <li>
            <Link to="/story" className={linkClass}>
              About RushDay
            </Link>
          </li>
        </ul>
      </div>
    </footer>
  )
}
