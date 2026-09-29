import type { ModuleDetail, ModuleSummary } from './common'

/**
 * Responses of the module routes (02-api.md section 8.2). `ModuleSummary` and `ModuleDetail`
 * themselves are shared shapes and live in `common.ts`.
 */

/** Where the module's semester stands for self-service enrolment in the current academic year. */
export type EnrolmentState = ModuleSummary['enrolmentState']

/**
 * `GET /api/modules`: every active module ordered by code, viewer-agnostic, served from a 30 s
 * cache (so `placesRemaining` can be up to 30 s old).
 */
export type CatalogueResponse = ModuleSummary[]

/**
 * `GET /api/modules/{code}`: the module row read uncached, so `enrolledCount` is live. An inactive
 * module is returned too (`isActive: false`).
 */
export type ModuleDetailResponse = ModuleDetail
