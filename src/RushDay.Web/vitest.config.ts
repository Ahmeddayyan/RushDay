import { fileURLToPath, URL } from 'node:url'

import react from '@vitejs/plugin-react'
import { defineConfig } from 'vitest/config'

// Kept separate from vite.config.ts on purpose: unit tests do not need Tailwind or the wwwroot output settings.
export default defineConfig({
  plugins: [react()],
  resolve: {
    alias: {
      '@': fileURLToPath(new URL('./src', import.meta.url)),
    },
  },
  test: {
    environment: 'jsdom',
    setupFiles: ['./src/test/setupTests.ts'],
    include: ['src/**/*.test.{ts,tsx}'],
    restoreMocks: true,
    unstubGlobals: true,
  },
})
