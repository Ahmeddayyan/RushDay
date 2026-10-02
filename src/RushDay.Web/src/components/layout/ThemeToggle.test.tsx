import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it, vi } from 'vitest'

import { THEME_STORAGE_KEY } from '@/lib/theme'

import { ThemeToggle } from './ThemeToggle'

function metaContent(media: string): string | null | undefined {
  return document.head
    .querySelector(`meta[name="theme-color"][media="${media}"]`)
    ?.getAttribute('content')
}

describe('ThemeToggle', () => {
  it('is a radiogroup of System, Light and Dark', () => {
    render(<ThemeToggle />)
    const group = screen.getByRole('radiogroup', { name: 'Theme' })
    expect(group).toBeInTheDocument()
    expect(screen.getByRole('radio', { name: 'System' })).toBeChecked()
    expect(screen.getAllByRole('radio')).toHaveLength(3)
  })

  it('applies and stores a choice, and mirrors it to theme-color', async () => {
    document.head.insertAdjacentHTML(
      'beforeend',
      '<meta name="theme-color" media="(prefers-color-scheme: light)" content="#f4f5f7"><meta name="theme-color" media="(prefers-color-scheme: dark)" content="#0f1216">',
    )
    render(<ThemeToggle />)

    await userEvent.click(screen.getByRole('radio', { name: 'Dark' }))
    expect(document.documentElement.dataset.theme).toBe('dark')
    expect(window.localStorage.getItem(THEME_STORAGE_KEY)).toBe('dark')
    expect(metaContent('(prefers-color-scheme: light)')).toBe('#0f1216')

    await userEvent.click(screen.getByRole('radio', { name: 'System' }))
    expect(document.documentElement.dataset.theme).toBeUndefined()
    expect(window.localStorage.getItem(THEME_STORAGE_KEY)).toBeNull()
    expect(metaContent('(prefers-color-scheme: light)')).toBe('#f4f5f7')
    expect(metaContent('(prefers-color-scheme: dark)')).toBe('#0f1216')

    for (const meta of document.head.querySelectorAll('meta[name="theme-color"]')) meta.remove()
  })

  it('still works when localStorage throws', async () => {
    vi.spyOn(Storage.prototype, 'getItem').mockImplementation(() => {
      throw new DOMException('denied', 'SecurityError')
    })
    vi.spyOn(Storage.prototype, 'setItem').mockImplementation(() => {
      throw new DOMException('full', 'QuotaExceededError')
    })
    vi.spyOn(Storage.prototype, 'removeItem').mockImplementation(() => {
      throw new DOMException('denied', 'SecurityError')
    })

    render(<ThemeToggle variant="compact" />)
    expect(screen.getByRole('radio', { name: 'System' })).toBeChecked()

    await userEvent.click(screen.getByRole('radio', { name: 'Light' }))
    expect(screen.getByRole('radio', { name: 'Light' })).toBeChecked()
    expect(document.documentElement.dataset.theme).toBe('light')

    await userEvent.click(screen.getByRole('radio', { name: 'System' }))
    expect(document.documentElement.dataset.theme).toBeUndefined()
  })

  it('keeps two toggles on the same page in step', async () => {
    render(
      <>
        <ThemeToggle label="Top bar theme" variant="compact" />
        <ThemeToggle label="Account theme" />
      </>,
    )
    const [topBarDark] = screen.getAllByRole('radio', { name: 'Dark' })
    if (!topBarDark) throw new Error('missing radio')
    await userEvent.click(topBarDark)
    for (const radio of screen.getAllByRole('radio', { name: 'Dark' })) expect(radio).toBeChecked()
  })
})
