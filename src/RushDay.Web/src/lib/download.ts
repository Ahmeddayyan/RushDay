import { apiFetch, type BlobResult } from '@/api/client'

/**
 * File downloads (CSV exports, JSON data exports, ICS calendars). The API streams attachments, so
 * the SPA fetches them through `apiFetch` (cookie session, same origin) and saves the blob through
 * a temporary object URL; labels say "downloads a file" (05-frontend.md section 12).
 */

/** Reads `filename="..."` (or RFC 5987 `filename*=UTF-8''...`) from Content-Disposition. */
export function filenameFromDisposition(headers: Headers, fallback: string): string {
  const disposition = headers.get('Content-Disposition') ?? ''
  const extended = /filename\*\s*=\s*UTF-8''([^;]+)/i.exec(disposition)
  if (extended?.[1]) {
    try {
      return decodeURIComponent(extended[1].trim())
    } catch {
      // fall through to the plain form
    }
  }
  const plain = /filename\s*=\s*"?([^";]+)"?/i.exec(disposition)
  return plain?.[1]?.trim() || fallback
}

/** Saves a blob under `filename`. The object URL is revoked on the next tick. */
export function downloadBlob(blob: Blob, filename: string): void {
  const url = URL.createObjectURL(blob)
  const anchor = document.createElement('a')
  anchor.href = url
  anchor.download = filename
  anchor.rel = 'noopener'
  anchor.style.display = 'none'
  document.body.append(anchor)
  anchor.click()
  anchor.remove()
  setTimeout(() => URL.revokeObjectURL(url), 0)
}

/** Builds a text file in the browser (the timetable's .ics) and saves it. */
export function downloadText(
  text: string,
  filename: string,
  type = 'text/plain;charset=utf-8',
): void {
  downloadBlob(new Blob([text], { type }), filename)
}

/**
 * GETs an attachment from the API and saves it, naming it from Content-Disposition. Resolves with
 * the response headers so callers can read, for example, `X-RushDay-Truncated`.
 */
export async function downloadFromApi(path: string, fallbackName: string): Promise<Headers> {
  const { blob, headers } = await apiFetch<BlobResult>(path, { expect: 'blob' })
  downloadBlob(blob, filenameFromDisposition(headers, fallbackName))
  return headers
}
