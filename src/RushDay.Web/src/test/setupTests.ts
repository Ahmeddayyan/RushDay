import '@testing-library/jest-dom/vitest'
import { cleanup } from '@testing-library/react'
import { afterEach, beforeEach, vi } from 'vitest'

// jsdom has no matchMedia. Default to a light OS theme; tests can override via setSystemDarkMode().
let systemPrefersDark = false

export function setSystemDarkMode(dark: boolean): void {
  systemPrefersDark = dark
}

function matchMedia(query: string): MediaQueryList {
  return {
    matches: query.includes('dark') && systemPrefersDark,
    media: query,
    onchange: null,
    addEventListener: vi.fn(),
    removeEventListener: vi.fn(),
    addListener: vi.fn(),
    removeListener: vi.fn(),
    dispatchEvent: vi.fn(() => false),
  }
}

beforeEach(() => {
  Object.defineProperty(window, 'matchMedia', {
    configurable: true,
    writable: true,
    value: matchMedia,
  })
})

afterEach(() => {
  cleanup()
  systemPrefersDark = false
  window.localStorage.clear()
  delete document.documentElement.dataset.theme
  document.title = ''
})
