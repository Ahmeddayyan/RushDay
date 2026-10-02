import { fileURLToPath, URL } from 'node:url'

import react from '@vitejs/plugin-react'
import { defineConfig } from 'vitest/config'

// Kept separate from vite.config.ts on purpose: unit tests do not need Tailwind or the wwwroot output settings.
export default defineConfig({
  plugins: [react()],
  resolve: { alias: { '@': fileURLToPath(new URL('./src', import.meta.url)) } },
  test: {
    environment: 'jsdom',
    setupFiles: ['./src/test/setup.ts'],
    include: ['src/**/*.test.{ts,tsx}'],
    exclude: ['e2e/**', 'node_modules/**'],
    restoreMocks: true,
    unstubGlobals: true,
    css: false,
    coverage: {
      provider: 'v8',
      reporter: ['text', 'lcov'],
      include: ['src/**'],
      exclude: ['src/test/**', 'src/**/*.test.*', 'src/main.tsx'],
      thresholds: {
        'src/api/**': { lines: 80 },
        'src/app/**': { lines: 80 },
        'src/lib/**': { lines: 80 },
      },
    },
  },
})
