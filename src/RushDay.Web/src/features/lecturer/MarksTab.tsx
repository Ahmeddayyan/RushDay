import { useSearchParams } from 'react-router'

import { formatDateTime } from '@/lib/format'
import { useDocumentTitle } from '@/lib/useDocumentTitle'

import { MarksGrid } from './components/MarksGrid'
import { SubmitDialog } from './components/SubmitDialog'
import { useMarks } from './hooks/useMarks'
import { useModuleContext } from './ModulePage'

/**
 * `/lecturer/modules/:code/marks` (05-frontend.md section 10): the grid plus the status banner and
 * the leader-only submit control. `headerQuery` reads the same `page=1, q=''` cache entry the grid
 * itself uses when that is also what is on screen, so this rarely costs a second request.
 */
export function Component() {
  const { module, timeZone } = useModuleContext()
  useDocumentTitle(`${module.code} marks · RushDay`)
  const [params, setParams] = useSearchParams()
  const q = params.get('q') ?? ''
  const page = Number(params.get('page') ?? '1')

  const headerQuery = useMarks(module.code, { q: '', page: 1 })
  const status = headerQuery.data?.status ?? module.marks.status
  const myRole = headerQuery.data?.myRole ?? module.myRole
  const leader = headerQuery.data?.leader ?? null
  const total = headerQuery.data?.summary.total ?? module.marks.total
  const submittedAt = headerQuery.data?.submittedAt ?? module.marks.submittedAt
  const publishedAt = headerQuery.data?.publishedAt ?? module.marks.publishedAt

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

  let banner: string | null = null
  if (status === 'submitted' && submittedAt) {
    banner = `Submitted on ${formatDateTime(submittedAt, timeZone)}. Marks are locked. Spotted an error? Ask the academic office to return this module to draft.`
  } else if (status === 'scheduled' && publishedAt) {
    banner = `Scheduled for publication on ${formatDateTime(publishedAt, timeZone)}. Students can't see these marks yet.`
  } else if (status === 'published' && publishedAt) {
    banner = `Published on ${formatDateTime(publishedAt, timeZone)}. Students can see these marks; the academic office corrects a single mark if one is wrong.`
  }

  const canOfferSubmit = status === 'draft' || status === 'noStudents'
  if (!banner && !canOfferSubmit)
    banner = "Marks for this module are locked and can't be edited here."

  return (
    <div className="flex flex-col gap-4">
      {banner && (
        <div
          role="status"
          className="rounded-md border border-border bg-surface-2 px-3.5 py-2.5 text-sm text-text"
        >
          {banner}
        </div>
      )}
      <MarksGrid
        code={module.code}
        status={status}
        page={page}
        q={q}
        onPageChange={setPage}
        onQueryChange={setQuery}
        {...(timeZone ? { timeZone } : {})}
        submitSlot={
          canOfferSubmit
            ? (unsaved) => (
                <SubmitDialog
                  code={module.code}
                  myRole={myRole}
                  leader={leader}
                  total={total}
                  unsaved={unsaved}
                  onSubmitted={() => void headerQuery.refetch()}
                />
              )
            : undefined
        }
      />
    </div>
  )
}
