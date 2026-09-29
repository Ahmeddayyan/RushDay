import { Tooltip } from '@/components/ui'
import { formatDateTime, formatRelative } from '@/lib/format'

/**
 * An instant shown absolute in the institution's zone, with the relative form ("3 days ago") in a
 * tooltip (05-frontend.md section 10 conventions). `zone` names the zone for windows and
 * publication instants, where the hour matters.
 */
export function Timestamp({
  iso,
  timeZone,
  zone = false,
  relative = true,
}: {
  iso: string
  timeZone: string
  zone?: boolean
  relative?: boolean
}) {
  const text = formatDateTime(iso, timeZone, { zone })
  if (!relative) return <time dateTime={iso}>{text}</time>
  return (
    <Tooltip content={formatRelative(iso)}>
      <time dateTime={iso} tabIndex={0} className="rounded-sm">
        {text}
      </time>
    </Tooltip>
  )
}
