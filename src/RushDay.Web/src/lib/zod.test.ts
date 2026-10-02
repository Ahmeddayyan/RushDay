import { describe, expect, it } from 'vitest'

import { z } from './zod'

// Every source file of the SPA as text (Vite's glob import; tests excluded).
const sources = import.meta.glob<string>(['../**/*.{ts,tsx}', '!../**/*.test.{ts,tsx}'], {
  query: '?raw',
  import: 'default',
  eager: true,
})

describe('lib/zod', () => {
  it('turns off the JIT, so no page reports a CSP eval violation', () => {
    expect(z.config().jitless).toBe(true)
  })

  it('is the only place that imports zod at runtime', () => {
    const files = Object.keys(sources)
    expect(files.length).toBeGreaterThan(100)
    const offenders = files.filter(
      (path) =>
        path !== './zod.ts' && /^import\s+(?!type\b)[^;\n]*from\s+'zod'/m.test(sources[path] ?? ''),
    )
    expect(offenders).toEqual([])
  })
})
