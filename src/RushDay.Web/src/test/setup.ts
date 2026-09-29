import '@testing-library/jest-dom/vitest'
import { cleanup } from '@testing-library/react'
import { afterAll, afterEach, beforeAll, beforeEach, vi } from 'vitest'

import { resetThemeForTests } from '@/lib/theme'

import { resetAuthMock } from './handlers/auth'
import { server } from './server'

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

// Radix primitives measure and observe elements; jsdom implements neither API.
class ResizeObserverStub {
  observe(): void {}
  unobserve(): void {}
  disconnect(): void {}
}

beforeAll(() => {
  server.listen({ onUnhandledRequest: 'error' })
})

beforeEach(() => {
  Object.defineProperty(window, 'matchMedia', {
    configurable: true,
    writable: true,
    value: matchMedia,
  })
  if (!('ResizeObserver' in window)) {
    Object.defineProperty(window, 'ResizeObserver', {
      configurable: true,
      writable: true,
      value: ResizeObserverStub,
    })
  }
  if (!Element.prototype.hasPointerCapture) {
    Element.prototype.hasPointerCapture = () => false
    Element.prototype.releasePointerCapture = () => {}
  }
  if (!Element.prototype.scrollIntoView) Element.prototype.scrollIntoView = () => {}
})

afterEach(() => {
  cleanup()
  server.resetHandlers()
  resetAuthMock()
  resetThemeForTests()
  systemPrefersDark = false
  window.localStorage.clear()
  window.sessionStorage.clear()
  delete document.documentElement.dataset.theme
  document.title = ''
})

afterAll(() => {
  server.close()
})
