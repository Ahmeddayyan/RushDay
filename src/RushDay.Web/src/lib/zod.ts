import { z } from 'zod'

/**
 * The one place the SPA imports zod's runtime from. Zod 4 compiles object parsers with
 * `new Function` when it can, and decides by probing `new Function('')` the first time a schema
 * is built. Under RushDay's CSP (`script-src 'self'`, no `'unsafe-eval'`, 03-security.md section 4)
 * that probe throws, which zod catches, but the browser still reports it as a
 * `securitypolicyviolation`, so every page with a form logged a CSP violation. `jitless` skips the
 * probe and the compiled fast path; parsing is unchanged and the forms here are small.
 *
 * Import `z` from here (`import { z } from '@/lib/zod'`), never from 'zod' directly, so this runs
 * before any schema is created; `import type { z } from 'zod'` is fine. `lib/zod.test.ts` checks.
 */
z.config({ jitless: true })

export { z }
