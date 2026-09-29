import { useSearchParams } from 'react-router'

import { Card, EmptyState, ErrorState, LoadingRegion, SearchInput, Skeleton } from '@/components/ui'
import { useDebouncedValue } from '@/lib/useDebouncedValue'
import { useDocumentTitle } from '@/lib/useDocumentTitle'

import { RosterTable } from './components/RosterTable'
import { useRoster } from './hooks/useRoster'
import { useModuleContext } from './ModulePage'

/** `/lecturer/modules/:code` (Roster tab, 05-frontend.md section 10): server-paged, searched. */
export function Component() {
  const { module, timeZone } = useModuleContext()
  useDocumentTitle(`${module.code} roster · RushDay`)
  const [params, setParams] = useSearchParams()
  const q = params.get('q') ?? ''
  const page = Number(params.get('page') ?? '1')
  const debouncedQ = useDebouncedValue(q, 250)

  const rosterQuery = useRoster(module.code, { q: debouncedQ, page })

  function setQuery(next: string) {
    setParams(
      (prev) => {
        const nextParams = new URLSearchParams(prev)
        if (next) nextParams.set('q', next)
        else nextParams.delete('q')
        nextParams.set('page', '1')
        return nextParams
      },
      { replace: true },
    )
  }

  function setPage(next: number) {
    setParams((prev) => {
      const nextParams = new URLSearchParams(prev)
      nextParams.set('page', String(next))
      return nextParams
    })
  }

  let content
  if (rosterQuery.isPending) {
    content = (
      <LoadingRegion label="roster">
        <div className="space-y-2 p-4">
          {[0, 1, 2].map((key) => (
            <Skeleton key={key} className="h-12 w-full" />
          ))}
        </div>
      </LoadingRegion>
    )
  } else if (rosterQuery.isError) {
    content = (
      <ErrorState
        error={rosterQuery.error}
        context={{ code: module.code }}
        onRetry={() => void rosterQuery.refetch()}
      />
    )
  } else if (rosterQuery.data.items.length === 0) {
    content = (
      <EmptyState
        title={
          q ? `No students match "${q}".` : `No students are enrolled on ${module.code} this year.`
        }
      />
    )
  } else {
    content = (
      <RosterTable
        code={module.code}
        rows={rosterQuery.data.items}
        page={rosterQuery.data.page}
        pageSize={rosterQuery.data.pageSize}
        total={rosterQuery.data.total}
        onPageChange={setPage}
        fetching={rosterQuery.isFetching}
        {...(timeZone ? { timeZone } : {})}
      />
    )
  }

  return (
    <div className="flex flex-col gap-4">
      <SearchInput
        label="Search students"
        value={q}
        onChange={setQuery}
        placeholder="Number or name"
        className="max-w-sm"
      />
      <Card flush={rosterQuery.isSuccess && rosterQuery.data.items.length > 0}>{content}</Card>
    </div>
  )
}
