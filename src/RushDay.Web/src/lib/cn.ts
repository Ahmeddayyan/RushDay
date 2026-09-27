export type ClassValue = string | number | null | undefined | false | ClassValue[]

/** Joins class names, dropping falsy values. Small on purpose: no dependency for a one-liner. */
export function cn(...inputs: ClassValue[]): string {
  const out: string[] = []
  for (const input of inputs) {
    if (!input && input !== 0) continue
    if (Array.isArray(input)) {
      const nested = cn(...input)
      if (nested) out.push(nested)
    } else {
      out.push(String(input))
    }
  }
  return out.join(' ')
}
