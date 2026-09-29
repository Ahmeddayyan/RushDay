import { useLoadResults as useLoadResultsQuery } from '@/api/endpoints/ops'
import type { LoadRun, LoadScenario, LoadVersion } from '@/api/types/loadResults'

/**
 * A small view model over `['load-results']` (05-frontend.md section 8) for the chart components:
 * grouping by scenario and picking a specific run out of it. Until v1 runs are committed,
 * `find(scenario, 'v1', ...)` always returns `undefined`, which every chart renders as
 * "not yet measured" rather than a zero or an empty gap.
 */
export interface LoadResultsView {
  isPending: boolean
  isError: boolean
  error: unknown
  machine: string | undefined
  generatedAt: string | undefined
  runs: LoadRun[]
  /** Every run of one scenario, in file order. */
  scenario: (scenario: LoadScenario) => LoadRun[]
  /** One run of a scenario and version, optionally narrowed by `targetRate` (dashboard-knee). */
  find: (scenario: LoadScenario, version: LoadVersion, targetRate?: number) => LoadRun | undefined
}

export function useLoadResults(): LoadResultsView {
  const query = useLoadResultsQuery()
  const runs = query.data?.runs ?? []

  return {
    isPending: query.isPending,
    isError: query.isError,
    error: query.error,
    machine: query.data?.machine,
    generatedAt: query.data?.generatedAt,
    runs,
    scenario: (scenario) => runs.filter((run) => run.scenario === scenario),
    find: (scenario, version, targetRate) =>
      runs.find(
        (run) =>
          run.scenario === scenario &&
          run.version === version &&
          (targetRate === undefined || run.targetRate === targetRate),
      ),
  }
}
