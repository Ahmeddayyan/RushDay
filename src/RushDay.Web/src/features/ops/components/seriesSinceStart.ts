import type { OpsSeriesPoint } from '@/api/types/ops'

const MINUTE_MS = 60_000

/**
 * The per-minute series covers the last 60 minutes whatever the uptime. Two kinds of bucket are
 * not history and would mislead on a chart:
 * - the minutes before the server started are empty, not a quiet hour, so the chart starts at the
 *   minute the server started ("History starts when the server last started");
 * - the minute still in progress when the sample was taken holds a few seconds of traffic and
 *   would draw as a sudden drop at the right edge, so it waits until it is complete.
 */
export function sinceStart(
  series: OpsSeriesPoint[],
  startedAt: string | undefined,
  sampledAt?: string,
): OpsSeriesPoint[] {
  const started = startedAt ? Date.parse(startedAt) : Number.NaN
  const sampled = sampledAt ? Date.parse(sampledAt) : Number.NaN
  const firstMinute = Number.isNaN(started) ? -Infinity : started - (started % MINUTE_MS)
  return series.filter((point) => {
    const minute = Date.parse(point.minute)
    if (minute < firstMinute) return false
    return Number.isNaN(sampled) || minute + MINUTE_MS <= sampled
  })
}
