/**
 * WCAG 2.x contrast (05-frontend.md section 9.1): relative luminance per
 * https://www.w3.org/TR/WCAG22/#dfn-relative-luminance and the (L1 + 0.05) / (L2 + 0.05) ratio.
 * Used by lib/contrast.test.ts to hold every token pair the components use for text to 4.5:1.
 */

export type Rgb = readonly [number, number, number]

/** Parses `#rgb` or `#rrggbb` (case-insensitive) into 0..255 channels. */
export function parseHex(hex: string): Rgb {
  const match = /^#?([0-9a-f]{3}|[0-9a-f]{6})$/i.exec(hex.trim())
  if (!match?.[1]) throw new Error(`Not a hex colour: ${hex}`)
  const digits =
    match[1].length === 3
      ? match[1]
          .split('')
          .map((d) => d + d)
          .join('')
      : match[1]
  const value = Number.parseInt(digits, 16)
  return [(value >> 16) & 255, (value >> 8) & 255, value & 255]
}

function channel(value: number): number {
  const c = value / 255
  return c <= 0.03928 ? c / 12.92 : ((c + 0.055) / 1.055) ** 2.4
}

/** Relative luminance, 0 (black) to 1 (white). */
export function relativeLuminance(colour: string | Rgb): number {
  const [r, g, b] = typeof colour === 'string' ? parseHex(colour) : colour
  return 0.2126 * channel(r) + 0.7152 * channel(g) + 0.0722 * channel(b)
}

/** The contrast ratio between two colours, from 1 (identical) to 21 (black on white); order does not matter. */
export function contrastRatio(a: string | Rgb, b: string | Rgb): number {
  const la = relativeLuminance(a)
  const lb = relativeLuminance(b)
  const [lighter, darker] = la >= lb ? [la, lb] : [lb, la]
  return (lighter + 0.05) / (darker + 0.05)
}

/** WCAG AA: 4.5:1 for normal text, 3:1 for large text and for non-text UI (1.4.11). */
export function meetsAA(
  foreground: string,
  background: string,
  kind: 'text' | 'non-text' = 'text',
): boolean {
  return contrastRatio(foreground, background) >= (kind === 'text' ? 4.5 : 3)
}
