import js from '@eslint/js'
import prettier from 'eslint-config-prettier/flat'
import reactHooks from 'eslint-plugin-react-hooks'
import reactRefresh from 'eslint-plugin-react-refresh'
import { defineConfig, globalIgnores } from 'eslint/config'
import globals from 'globals'
import tseslint from 'typescript-eslint'

export default defineConfig([
  globalIgnores(['dist', 'coverage', 'playwright-report', 'test-results', 'node_modules']),
  {
    files: ['**/*.{ts,tsx}'],
    extends: [
      js.configs.recommended,
      tseslint.configs.recommended,
      reactHooks.configs.flat.recommended,
      reactRefresh.configs.vite,
      // Last, so formatting is Prettier's job alone and never a lint error.
      prettier,
    ],
    languageOptions: {
      globals: globals.browser,
    },
  },
  {
    // Node-side config and Playwright specs.
    files: ['*.config.ts', 'tests/**/*.ts'],
    languageOptions: {
      globals: { ...globals.browser, ...globals.node },
    },
  },
])
