/** "3h 12m" / "45m" / "12s": a short human duration for uptimes and sample ages (ops-only). */
export function formatUptime(totalSeconds: number): string {
  const seconds = Math.max(0, Math.round(totalSeconds))
  const days = Math.floor(seconds / 86_400)
  const hours = Math.floor((seconds % 86_400) / 3600)
  const minutes = Math.floor((seconds % 3600) / 60)
  const secs = seconds % 60

  if (days > 0) return `${days}d ${hours}h`
  if (hours > 0) return `${hours}h ${minutes}m`
  if (minutes > 0) return `${minutes}m`
  return `${secs}s`
}
