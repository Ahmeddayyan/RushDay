import type { ModuleSummary, Semester } from '@/api/types/common'
import { DEPARTMENTS, departmentOf, type DepartmentCode } from '@/lib/moduleCode'

/** The catalogue's filters (05-frontend.md section 10, `/student/modules`), kept in the URL. */

export const SEMESTERS: readonly Semester[] = ['autumn', 'spring']
export const LEVELS = ['1', '2', '3'] as const
export const DEPARTMENT_CODES = Object.keys(DEPARTMENTS) as DepartmentCode[]

export interface CatalogueFilters {
  q: string
  semester: Semester | null
  level: (typeof LEVELS)[number] | null
  dept: DepartmentCode | null
  availability: 'available' | 'full' | null
  mine: boolean
}

function oneOf<T extends string>(value: string | null, allowed: readonly T[]): T | null {
  return value !== null && (allowed as readonly string[]).includes(value) ? (value as T) : null
}

/** The filters in the URL (`?q=&semester=&level=&dept=&availability=&mine=`); unknown values are ignored. */
export function readFilters(params: URLSearchParams): CatalogueFilters {
  return {
    q: (params.get('q') ?? '').trim(),
    semester: oneOf(params.get('semester'), SEMESTERS),
    level: oneOf(params.get('level'), LEVELS),
    dept: oneOf(params.get('dept'), DEPARTMENT_CODES),
    availability: oneOf(params.get('availability'), ['available', 'full'] as const),
    mine: params.get('mine') === '1',
  }
}

export function filterModules(
  modules: readonly ModuleSummary[],
  filters: CatalogueFilters,
  enrolled: ReadonlySet<string>,
): ModuleSummary[] {
  const q = filters.q.toLowerCase()
  return modules.filter((module) => {
    if (q && !module.code.toLowerCase().includes(q) && !module.title.toLowerCase().includes(q)) {
      return false
    }
    if (filters.semester && module.semester !== filters.semester) return false
    if (filters.level && String(module.level) !== filters.level) return false
    if (filters.dept && (module.department || departmentOf(module.code)) !== filters.dept) {
      return false
    }
    if (filters.availability === 'available' && module.placesRemaining <= 0) return false
    if (filters.availability === 'full' && module.placesRemaining > 0) return false
    if (filters.mine && !enrolled.has(module.code)) return false
    return true
  })
}

export function hasFilters(filters: CatalogueFilters): boolean {
  return Boolean(
    filters.q ||
    filters.semester ||
    filters.level ||
    filters.dept ||
    filters.availability ||
    filters.mine,
  )
}
