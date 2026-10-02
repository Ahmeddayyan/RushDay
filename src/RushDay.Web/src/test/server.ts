import { setupServer } from 'msw/node'

import { authHandlers } from './handlers/auth'
import { publicHandlers } from './handlers/public'

/**
 * The MSW server for every Vitest test (05-frontend.md section 13.1): only the auth and public
 * handlers. Feature tests add their own through `renderWithProviders(ui, { handlers })`, which calls
 * `server.use(...)`; `server.resetHandlers()` runs after each test (src/test/setup.ts), so stages
 * S7-S10 never edit this file.
 */
export const server = setupServer(...authHandlers, ...publicHandlers)
