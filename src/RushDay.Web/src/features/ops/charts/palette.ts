import { useTheme } from '@/lib/theme'

/**
 * Chart colour tokens (05-frontend.md section 9.1 and 11: "validate against RushDay's surfaces, then
 * marks"). Most read the app's own `--chart-*` custom properties straight off the root
 * (`styles/app.css`, owned by stage S5) as CSS `var()` strings, which the browser resolves against
 * the active theme; Recharts accepts these directly as `fill`/`stroke`.
 *
 * Two of S5's tokens fail the 3:1 non-text contrast WCAG 1.4.11 needs for a chart mark against a
 * surface (light `--chart-accent-2` is 2.11:1 on `--surface`; dark `--chart-accent-3` is 2.13:1 on
 * `--surface`, S5's reported known issue). This stage may not edit `styles/app.css`, so
 * `CHART_ACCENT_2_SAFE` and `CHART_ACCENT_3_SAFE` below replace them with chart-only literals,
 * validated by `palette.test.ts` to hold >=3:1 against both `--surface` and `--background` in both
 * themes. They cannot be plain CSS custom properties without touching app.css's `:root` blocks, so
 * `useChartPalette()` resolves the right literal for the active theme with `useTheme()` instead;
 * every chart that needs the "turned away" (accent-2 job) or third ordinal step (accent-3 job)
 * colour uses this hook rather than `var(--chart-accent-2)` / `var(--chart-accent-3)` directly.
 */

export const CHART_COLORS = {
  accent: 'var(--chart-accent)',
  muted: 'var(--chart-muted)',
  grid: 'var(--chart-grid)',
  critical: 'var(--chart-critical)',
} as const

/** Replaces `--chart-accent-2`. Dark keeps S5's own value, which already passes. */
export const CHART_ACCENT_2_SAFE: Record<'light' | 'dark', string> = {
  light: '#4d7fce',
  dark: '#6da7ec',
}

/** Replaces `--chart-accent-3`. Light keeps S5's own value, which already passes. */
export const CHART_ACCENT_3_SAFE: Record<'light' | 'dark', string> = {
  light: '#104281',
  dark: '#3f73c9',
}

export interface ChartPalette {
  accent: string
  muted: string
  grid: string
  critical: string
  /** Safe replacement for the "turned away"/second-emphasis job (was `--chart-accent-2`). */
  accent2: string
  /** Safe replacement for the third ordinal-step job (was `--chart-accent-3`). */
  accent3: string
}

/** The chart palette resolved for the page's current theme (05-frontend.md section 11). */
export function useChartPalette(): ChartPalette {
  const { resolved } = useTheme()
  return {
    ...CHART_COLORS,
    accent2: CHART_ACCENT_2_SAFE[resolved],
    accent3: CHART_ACCENT_3_SAFE[resolved],
  }
}
