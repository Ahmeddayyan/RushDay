import { describe, expect, it } from 'vitest'

import { contrastRatio, meetsAA, parseHex, relativeLuminance } from './contrast'

/**
 * Holds the design tokens of styles/app.css to WCAG 2.2 AA in both themes (05-frontend.md section 9.1):
 * every pair a component uses for text at 4.5:1, and the non-text colours that carry meaning (the
 * focus ring, form-control borders, chart marks) at 3:1. The values are parsed from the stylesheet,
 * so changing a token without passing this test is impossible.
 */

type Tokens = Record<string, string>

// Vitest runs with css: false, so a ?raw import of a stylesheet is empty; read the file with Node's fs
// (process.getBuiltinModule keeps this file free of Node type imports in the browser tsconfig). Vitest's
// working directory is src/RushDay.Web.
const nodeProcess = (
  globalThis as unknown as {
    process: {
      cwd(): string
      getBuiltinModule(id: 'node:fs'): { readFileSync(path: string, encoding: 'utf8'): string }
    }
  }
).process
const css = nodeProcess
  .getBuiltinModule('node:fs')
  .readFileSync(`${nodeProcess.cwd()}/src/styles/app.css`, 'utf8')

function parseBlock(source: string, selector: string): Tokens {
  const start = source.indexOf(selector)
  if (start === -1) throw new Error(`selector not found: ${selector}`)
  const open = source.indexOf('{', start)
  const close = source.indexOf('}', open)
  const tokens: Tokens = {}
  for (const match of source
    .slice(open + 1, close)
    .matchAll(/--([a-z0-9-]+)\s*:\s*(#[0-9a-f]{3,6})\b/gi)) {
    const [, name, value] = match
    if (name && value) tokens[name] = value.toLowerCase()
  }
  return tokens
}

const light = parseBlock(css, ':root {')
const darkMedia = parseBlock(css, ":root:not([data-theme='light'])")
const dark = parseBlock(css, ":root[data-theme='dark']")

/** [foreground, background] token pairs used for text (Badge, StatTile captions, FormField hints, ...). */
const textPairs: [string, string][] = [
  ['text', 'surface'],
  ['text', 'background'],
  ['text', 'surface-2'],
  ['muted', 'surface'],
  ['muted', 'surface-2'],
  ['subtle', 'surface'],
  ['success', 'success-soft'],
  ['success', 'surface'],
  ['warning', 'warning-soft'],
  ['warning', 'surface'],
  ['danger', 'danger-soft'],
  ['danger', 'surface'],
  ['info', 'info-soft'],
  ['info', 'surface'],
  ['primary', 'primary-soft'],
  ['primary', 'surface'],
  ['primary-foreground', 'primary'],
]

/** Light-only extras from the table in section 9.1 (dark lists these pairs on --surface only). */
const lightOnlyTextPairs: [string, string][] = [
  ['muted', 'background'],
  ['subtle', 'surface-2'],
  ['subtle', 'background'],
]

/** Non-text colours that identify something (WCAG 1.4.11): 3:1 against the surfaces they sit on. */
const nonTextPairs: [string, string][] = [
  ['focus', 'surface'],
  ['focus', 'background'],
  ['subtle', 'surface'], // form-control borders (components/ui/field.ts)
  ['chart-accent', 'surface'],
  ['chart-muted', 'surface'],
  ['chart-critical', 'surface'],
]

function ratio(tokens: Tokens, foreground: string, background: string): number {
  const fg = tokens[foreground]
  const bg = tokens[background]
  if (!fg || !bg) throw new Error(`missing token --${foreground} or --${background}`)
  return contrastRatio(fg, bg)
}

describe('contrast helpers', () => {
  it('computes the WCAG ratio', () => {
    expect(contrastRatio('#000000', '#ffffff')).toBeCloseTo(21, 5)
    expect(contrastRatio('#ffffff', '#ffffff')).toBe(1)
    expect(contrastRatio('#fff', '#000')).toBeCloseTo(21, 5)
    expect(relativeLuminance([255, 255, 255])).toBe(1)
    expect(meetsAA('#5f6672', '#ffffff')).toBe(true)
    expect(meetsAA('#c9ced6', '#ffffff', 'non-text')).toBe(false)
  })

  it('rejects something that is not a hex colour', () => {
    expect(() => parseHex('rebeccapurple')).toThrow('Not a hex colour')
  })
})

describe('design tokens (styles/app.css)', () => {
  it('defines the dark theme identically for the media query and for data-theme="dark"', () => {
    expect(Object.keys(dark).length).toBeGreaterThan(20)
    expect(darkMedia).toEqual(dark)
  })

  it.each([
    ['light', light],
    ['dark', dark],
  ] as const)('keeps every text pair at 4.5:1 or more in the %s theme', (_, tokens) => {
    for (const [foreground, background] of textPairs) {
      expect(
        ratio(tokens, foreground, background),
        `--${foreground} on --${background}`,
      ).toBeGreaterThanOrEqual(4.5)
    }
  })

  it('keeps the light-only pairs of the spec table at 4.5:1 or more', () => {
    for (const [foreground, background] of lightOnlyTextPairs) {
      expect(
        ratio(light, foreground, background),
        `--${foreground} on --${background}`,
      ).toBeGreaterThanOrEqual(4.5)
    }
  })

  it.each([
    ['light', light],
    ['dark', dark],
  ] as const)('keeps meaningful non-text colours at 3:1 or more in the %s theme', (_, tokens) => {
    for (const [foreground, background] of nonTextPairs) {
      expect(
        ratio(tokens, foreground, background),
        `--${foreground} on --${background}`,
      ).toBeGreaterThanOrEqual(3)
    }
  })

  it('matches the ratios the spec table quotes', () => {
    expect(ratio(light, 'text', 'surface')).toBeGreaterThanOrEqual(15)
    expect(ratio(dark, 'text', 'surface')).toBeGreaterThanOrEqual(13)
    expect(ratio(light, 'primary-foreground', 'primary')).toBeCloseTo(5.5, 1)
  })
})
