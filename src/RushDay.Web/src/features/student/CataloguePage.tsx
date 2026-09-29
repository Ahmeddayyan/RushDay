import { useEffect, useMemo, useRef, useState } from 'react'
import { CalendarClock, Info, SearchX } from 'lucide-react'
import { useSearchParams } from 'react-router'

import { usePublicStatus } from '@/api/endpoints/public'
import {
  Button,
  Card,
  Checkbox,
  EmptyState,
  ErrorState,
  FormField,
  LoadingRegion,
  PageHeader,
  Refetching,
  SearchInput,
  Select,
  Skeleton,
  SkeletonText,
} from '@/components/ui'
import { DEFAULT_TIME_ZONE } from '@/lib/format'
import { DEPARTMENTS } from '@/lib/moduleCode'
import { useDebouncedValue } from '@/lib/useDebouncedValue'
import { useDocumentTitle } from '@/lib/useDocumentTitle'

import {
  DEPARTMENT_CODES,
  filterModules,
  hasFilters,
  LEVELS,
  readFilters,
  SEMESTERS,
  type CatalogueFilters,
} from './catalogueFilters'
import { CreditBudget } from './components/CreditBudget'
import { ModuleCard } from './components/ModuleCard'
import { moduleWindowOf, windowClosedSentence } from './copy'
import { creditsUsed, enrolledThisYear, findRow } from './enrolState'
import { useCatalogue } from './hooks/useCatalogue'
import { useCachedDashboard } from './hooks/useDashboard'
import { useMyEnrolments } from './hooks/useMyEnrolments'

function CatalogueSkeleton() {
  return (
    <LoadingRegion label="modules">
      <div className="grid gap-4 md:grid-cols-2 xl:grid-cols-3">
        {[0, 1, 2, 3, 4, 5].map((key) => (
          <div key={key} className="rounded-lg border border-border bg-surface p-4 md:p-6">
            <Skeleton className="mb-2 h-4 w-16" />
            <Skeleton className="mb-3 h-6 w-3/4" />
            <SkeletonText lines={2} className="mb-4" />
            <Skeleton className="h-10 w-36" />
          </div>
        ))}
      </div>
    </LoadingRegion>
  )
}

/**
 * `/student/modules` (05-frontend.md section 10): the catalogue with URL-synced filters, a live
 * result count, the window banners, both semesters' credit budgets and a card per module with its
 * enrol control. Data: `['modules','catalogue']` and `['student','enrolments']`.
 */
export function Component() {
  useDocumentTitle('Modules · RushDay')
  const [params, setParams] = useSearchParams()
  const filters = readFilters(params)

  // The search box types freely; the URL (and the list) follow 250 ms after the typing stops.
  const [search, setSearch] = useState(filters.q)
  const debounced = useDebouncedValue(search, 250)
  const [urlQ, setUrlQ] = useState(filters.q)
  if (filters.q !== urlQ) {
    // The URL changed from outside the box (Back, Clear filters): show what the URL says.
    setUrlQ(filters.q)
    if (filters.q !== debounced.trim()) setSearch(filters.q)
  }
  const writtenRef = useRef(debounced)
  useEffect(() => {
    if (writtenRef.current === debounced) return
    writtenRef.current = debounced
    const value = debounced.trim()
    setParams(
      (previous) => {
        if ((previous.get('q') ?? '') === value) return previous
        const next = new URLSearchParams(previous)
        if (value) next.set('q', value)
        else next.delete('q')
        return next
      },
      { replace: true },
    )
  }, [debounced, setParams])

  const setFilter = (name: keyof CatalogueFilters, value: string | null) => {
    setParams(
      (previous) => {
        const next = new URLSearchParams(previous)
        if (value) next.set(name, value)
        else next.delete(name)
        return next
      },
      { replace: true },
    )
  }

  const clearFilters = () => {
    setSearch('')
    setParams(new URLSearchParams(), { replace: true })
  }

  const catalogue = useCatalogue()
  const { query: enrolments, isFresh } = useMyEnrolments()
  const { data: status } = usePublicStatus()
  const { data: dashboard } = useCachedDashboard()
  const timeZone = status?.institution.timeZone ?? DEFAULT_TIME_ZONE
  const currentYear = status?.academicYear ?? dashboard?.academicYear ?? null

  const enrolled = useMemo(
    () => enrolledThisYear(enrolments.data, currentYear),
    [enrolments.data, currentYear],
  )
  const modules = catalogue.data ?? []
  const visible = filterModules(modules, filters, enrolled)
  const filtered = hasFilters(filters)

  const banners = status
    ? SEMESTERS.flatMap((semester) => {
        const window = status.enrolmentWindows.find(
          (item) => item.semester === semester && item.academicYear === status.academicYear,
        )
        const sentence = windowClosedSentence(moduleWindowOf(semester, window), timeZone)
        return sentence ? [{ semester, sentence }] : []
      })
    : []

  let content
  if (catalogue.isPending || enrolments.isPending) {
    content = <CatalogueSkeleton />
  } else if (catalogue.isError || enrolments.isError) {
    const failed = catalogue.isError ? catalogue : enrolments
    content = (
      <Card>
        <ErrorState error={failed.error} onRetry={() => void failed.refetch()} />
      </Card>
    )
  } else if (visible.length === 0) {
    content = (
      <Card>
        <EmptyState
          icon={SearchX}
          title="No modules match these filters"
          description="Try a different search, or clear the filters to see every module."
          action={
            <Button variant="secondary" onClick={clearFilters}>
              Clear filters
            </Button>
          }
        />
      </Card>
    )
  } else {
    content = (
      <Refetching active={catalogue.isFetching && !catalogue.isPending}>
        <ul className="grid gap-4 md:grid-cols-2 xl:grid-cols-3">
          {visible.map((module) => (
            <li key={module.code} className="min-w-0">
              <ModuleCard
                module={module}
                row={findRow(enrolments.data, module.code)}
                currentYear={currentYear}
                creditsUsed={creditsUsed(enrolments.data, module.semester, currentYear)}
                enrolmentsFresh={isFresh}
                completed={
                  dashboard
                    ? (dashboard.completed.find((item) => item.code === module.code) ?? null)
                    : undefined
                }
                timeZone={timeZone}
              />
            </li>
          ))}
        </ul>
      </Refetching>
    )
  }

  const loaded = !catalogue.isPending && !catalogue.isError
  const countText = filtered
    ? `${visible.length} of ${modules.length} ${modules.length === 1 ? 'module' : 'modules'}`
    : `${modules.length} ${modules.length === 1 ? 'module' : 'modules'}`

  return (
    <>
      <PageHeader
        title="Modules"
        description="Browse this year's modules, see places left and enrol."
      />

      <div className="mb-6 flex flex-col gap-4">
        {banners.map(({ semester, sentence }) => (
          <p
            key={semester}
            className="flex items-start gap-2 rounded-lg border border-info/25 bg-info-soft px-4 py-3 text-sm text-info"
          >
            <CalendarClock aria-hidden="true" className="mt-0.5 size-4 shrink-0" />
            {sentence}
          </p>
        ))}

        {status && enrolments.data && (
          <Card className="grid gap-4 sm:grid-cols-2">
            {SEMESTERS.map((semester) => (
              <CreditBudget
                key={semester}
                semester={semester}
                used={creditsUsed(enrolments.data, semester, currentYear)}
                academicYear={status.academicYear}
              />
            ))}
          </Card>
        )}

        <form
          role="search"
          aria-label="Filter modules"
          onSubmit={(event) => event.preventDefault()}
          className="grid grid-cols-2 gap-3 lg:grid-cols-[minmax(0,2fr)_repeat(4,minmax(0,1fr))]"
        >
          <SearchInput
            label="Search by code or title"
            showLabel
            value={search}
            onChange={setSearch}
            placeholder="CS3099 or Databases"
            className="col-span-2 lg:col-span-1"
          />
          <FormField label="Semester">
            <Select
              value={filters.semester ?? ''}
              onChange={(event) => setFilter('semester', event.target.value || null)}
              options={[
                { value: '', label: 'All' },
                { value: 'autumn', label: 'Autumn' },
                { value: 'spring', label: 'Spring' },
              ]}
            />
          </FormField>
          <FormField label="Level">
            <Select
              value={filters.level ?? ''}
              onChange={(event) => setFilter('level', event.target.value || null)}
              options={[
                { value: '', label: 'All' },
                ...LEVELS.map((level) => ({ value: level, label: `Level ${level}` })),
              ]}
            />
          </FormField>
          <FormField label="Department">
            <Select
              value={filters.dept ?? ''}
              onChange={(event) => setFilter('dept', event.target.value || null)}
              options={[
                { value: '', label: 'All' },
                ...DEPARTMENT_CODES.map((code) => ({
                  value: code,
                  label: `${code} · ${DEPARTMENTS[code]}`,
                })),
              ]}
            />
          </FormField>
          <FormField label="Availability">
            <Select
              value={filters.availability ?? ''}
              onChange={(event) => setFilter('availability', event.target.value || null)}
              options={[
                { value: '', label: 'All' },
                { value: 'available', label: 'Places available' },
                { value: 'full', label: 'Full' },
              ]}
            />
          </FormField>
        </form>

        <div className="flex flex-wrap items-center justify-between gap-x-6 gap-y-2">
          <Checkbox
            label="Enrolled only"
            checked={filters.mine}
            onChange={(event) => setFilter('mine', event.target.checked ? '1' : null)}
          />
          {filtered && (
            <Button variant="ghost" size="sm" onClick={clearFilters}>
              Clear filters
            </Button>
          )}
        </div>

        <div className="flex flex-wrap items-baseline justify-between gap-x-6 gap-y-1">
          <p aria-live="polite" className="text-sm font-medium text-text">
            {loaded ? countText : ''}
          </p>
          <p className="flex items-center gap-1.5 text-sm text-muted">
            <Info aria-hidden="true" className="size-4 shrink-0" />
            Places update every 30 seconds. Open a module for live numbers.
          </p>
        </div>
      </div>

      {content}
    </>
  )
}
