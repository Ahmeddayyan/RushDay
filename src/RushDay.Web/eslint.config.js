import js from '@eslint/js'
import prettier from 'eslint-config-prettier/flat'
import reactHooks from 'eslint-plugin-react-hooks'
import reactRefresh from 'eslint-plugin-react-refresh'
import { defineConfig, globalIgnores } from 'eslint/config'
import globals from 'globals'
import tseslint from 'typescript-eslint'

export default defineConfig([
  globalIgnores([
    'dist',
    'coverage',
    'playwright-report',
    'test-results',
    'blob-report',
    'node_modules',
  ]),
  {
    files: ['**/*.{ts,tsx}'],
    extends: [
      js.configs.recommended,
      tseslint.configs.recommendedTypeChecked,
      reactHooks.configs.flat.recommended,
      reactRefresh.configs.vite,
      // Last, so formatting is Prettier's job alone and never a lint error.
      prettier,
    ],
    languageOptions: {
      globals: globals.browser,
      parserOptions: {
        projectService: true,
        tsconfigRootDir: import.meta.dirname,
      },
    },
    rules: {
      // CSP is script-src 'self' with no inline scripts (03-security.md section 4): innerHTML is the
      // one way React code could still inject markup, so it is refused outright.
      'no-restricted-syntax': [
        'error',
        {
          selector: 'JSXAttribute[name.name="dangerouslySetInnerHTML"]',
          message: 'render text; never HTML',
        },
      ],
      // The session cookie is HttpOnly (T2, 03-security.md section 2): the SPA has no business reading it.
      'no-restricted-properties': [
        'error',
        {
          object: 'document',
          property: 'cookie',
          message: 'the session cookie is HttpOnly and never readable from script',
        },
      ],
    },
  },
  {
    // Node-side config and Playwright specs.
    files: ['*.config.ts', 'e2e/**/*.ts'],
    languageOptions: {
      globals: { ...globals.browser, ...globals.node },
    },
  },
])
