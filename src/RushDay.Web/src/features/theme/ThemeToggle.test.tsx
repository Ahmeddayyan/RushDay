import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it, vi } from 'vitest'

import { setSystemDarkMode } from '@/test/setupTests'

import { THEME_STORAGE_KEY } from './ThemeContext'
import { ThemeProvider } from './ThemeProvider'
import { ThemeToggle } from './ThemeToggle'

function renderToggle() {
  return render(
    <ThemeProvider>
      <ThemeToggle />
    </ThemeProvider>,
  )
}

describe('ThemeToggle', () => {
  it('follows the OS preference when nothing is stored', () => {
    setSystemDarkMode(true)
    renderToggle()

    expect(document.documentElement.dataset.theme).toBe('dark')
    expect(screen.getByRole('button', { name: 'Switch to light theme' })).toBeInTheDocument()
    expect(window.localStorage.getItem(THEME_STORAGE_KEY)).toBeNull()
  })

  it('toggles the theme and persists the choice', async () => {
    const user = userEvent.setup()
    renderToggle()
    expect(document.documentElement.dataset.theme).toBe('light')

    await user.click(screen.getByRole('button', { name: 'Switch to dark theme' }))

    expect(document.documentElement.dataset.theme).toBe('dark')
    expect(window.localStorage.getItem(THEME_STORAGE_KEY)).toBe('dark')
    expect(screen.getByRole('button', { name: 'Switch to light theme' })).toBeInTheDocument()

    await user.click(screen.getByRole('button', { name: 'Switch to light theme' }))

    expect(document.documentElement.dataset.theme).toBe('light')
    expect(window.localStorage.getItem(THEME_STORAGE_KEY)).toBe('light')
  })

  it('honours a stored preference over the OS', () => {
    setSystemDarkMode(true)
    window.localStorage.setItem(THEME_STORAGE_KEY, 'light')
    renderToggle()

    expect(document.documentElement.dataset.theme).toBe('light')
  })

  it('still toggles when localStorage throws', async () => {
    const user = userEvent.setup()
    vi.spyOn(Storage.prototype, 'getItem').mockImplementation(() => {
      throw new DOMException('denied', 'SecurityError')
    })
    vi.spyOn(Storage.prototype, 'setItem').mockImplementation(() => {
      throw new DOMException('denied', 'QuotaExceededError')
    })
    renderToggle()

    await user.click(screen.getByRole('button', { name: 'Switch to dark theme' }))

    expect(document.documentElement.dataset.theme).toBe('dark')
  })
})
