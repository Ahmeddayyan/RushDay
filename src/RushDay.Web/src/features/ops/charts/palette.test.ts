import { readFileSync } from 'node:fs'
import path from 'node:path'
import { describe, expect, it } from 'vitest'

import { contrastRatio } from '@/lib/contrast'

import { CHART_ACCENT_2_SAFE, CHART_ACCENT_3_SAFE } from './palette'

/**
 * `CHART_ACCENT_2_SAFE`/`CHART_ACCENT_3_SAFE` (palette.ts) exist to fix a known S5 contrast failure
 * without editing `styles/app.css` (not owned by this stage): light `--chart-accent-2` is 2.11:1 on
 * `--surface` and dark `--chart-accent-3` is 2.13:1 on `--surface`, both below the 3:1 WCAG 1.4.11
 * floor for a non-text graphical mark. Rather than duplicate `--surface`/`--background` as literals
 * here (which would drift if S5 changes them), this test reads the live values straight out of
 * app.css: the first `--background`/`--surface` pair is the light `:root` block, the last is the
 * `:root[data-theme='dark']` block (05-frontend.md section 9.1).
 */

// vitest.config.ts runs with the working directory at the project root (src/RushDay.Web).
const appCssPath = path.join(process.cwd(), 'src', 'styles', 'app.css')
const appCss = readFileSync(appCssPath, 'utf8')

function tokenValues(name: string): string[] {
  return [...appCss.matchAll(new RegExp(`--${name}:\\s*(#[0-9a-fA-F]{3,8})`, 'g'))].map(
    (match) => match[1] ?? '',
  )
}

const backgrounds = tokenValues('background')
const surfaces = tokenValues('surface')
if (backgrounds.length < 2 || surfaces.length < 2) {
  throw new Error(
    'Could not find both the light and dark --background/--surface tokens in app.css.',
  )
}

const LIGHT_SURFACES = [surfaces[0], backgrounds[0]] as [string, string]
const DARK_SURFACES = [surfaces.at(-1), backgrounds.at(-1)] as [string, string]

describe('chart palette contrast (WCAG 1.4.11: >=3:1 for a graphical mark)', () => {
  it('confirms the known issue this module fixes: S5’s raw tokens fall short', () => {
    expect(contrastRatio('#86b6ef', LIGHT_SURFACES[0])).toBeLessThan(3) // light --chart-accent-2
    expect(contrastRatio('#184f95', DARK_SURFACES[0])).toBeLessThan(3) // dark --chart-accent-3
  })

  it('CHART_ACCENT_2_SAFE holds >=3:1 against both surfaces, in both themes', () => {
    for (const surface of LIGHT_SURFACES) {
      expect(contrastRatio(CHART_ACCENT_2_SAFE.light, surface)).toBeGreaterThanOrEqual(3)
    }
    for (const surface of DARK_SURFACES) {
      expect(contrastRatio(CHART_ACCENT_2_SAFE.dark, surface)).toBeGreaterThanOrEqual(3)
    }
  })

  it('CHART_ACCENT_3_SAFE holds >=3:1 against both surfaces, in both themes', () => {
    for (const surface of LIGHT_SURFACES) {
      expect(contrastRatio(CHART_ACCENT_3_SAFE.light, surface)).toBeGreaterThanOrEqual(3)
    }
    for (const surface of DARK_SURFACES) {
      expect(contrastRatio(CHART_ACCENT_3_SAFE.dark, surface)).toBeGreaterThanOrEqual(3)
    }
  })
})
