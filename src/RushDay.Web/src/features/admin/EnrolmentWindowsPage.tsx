import { useState } from 'react'
import { CalendarClock, Plus } from 'lucide-react'

import {
  Button,
  Card,
  EmptyState,
  ErrorState,
  LoadingRegion,
  PageHeader,
  Refetching,
  Skeleton,
} from '@/components/ui'
import { useDocumentTitle } from '@/lib/useDocumentTitle'

import { WindowEditor } from './components/WindowEditor'
import { useWindows } from './hooks/useWindows'
import { useInstitutionClock } from './lib/useInstitutionClock'

/**
 * `/admin/enrolment` (05-frontend.md section 10): one `WindowEditor` card per window, newest year
 * first, "Add window", and the open-now / close-now shortcuts. Times are the institution's.
 */
export function Component() {
  useDocumentTitle('Enrolment windows · RushDay')
  const { timeZone, academicYear, now } = useInstitutionClock()
  const query = useWindows()
  const [adding, setAdding] = useState(false)

  let content
  if (query.isPending) {
    content = (
      <LoadingRegion label="enrolment windows">
        <div className="flex flex-col gap-6">
          {[0, 1].map((key) => (
            <Skeleton key={key} className="h-56 w-full rounded-lg" />
          ))}
        </div>
      </LoadingRegion>
    )
  } else if (query.isError) {
    content = (
      <Card>
        <ErrorState error={query.error} onRetry={() => void query.refetch()} />
      </Card>
    )
  } else if (query.data.length === 0 && !adding) {
    content = (
      <Card>
        <EmptyState
          icon={CalendarClock}
          title="No enrolment windows yet."
          description="Students can enrol themselves only while a window for the module's semester is open."
          action={<Button onClick={() => setAdding(true)}>Add window</Button>}
        />
      </Card>
    )
  } else {
    content = (
      <Refetching active={query.isFetching && !query.isPending}>
        <div className="flex flex-col gap-6">
          {adding && (
            <WindowEditor
              window={null}
              timeZone={timeZone}
              now={now}
              defaultAcademicYear={academicYear ?? ''}
              onDone={() => setAdding(false)}
            />
          )}
          {query.data.map((window) => (
            <WindowEditor
              key={`${window.id}-${window.opensAt}-${window.closesAt}-${window.withdrawalDeadlineAt}`}
              window={window}
              timeZone={timeZone}
              now={now}
            />
          ))}
        </div>
      </Refetching>
    )
  }

  return (
    <>
      <PageHeader
        title="Enrolment windows"
        description={`When students can enrol themselves, per academic year and semester. Times are in ${timeZone}.`}
        actions={
          <Button onClick={() => setAdding(true)} disabled={adding}>
            <Plus aria-hidden="true" className="size-4" />
            Add window
          </Button>
        }
      />
      {content}
    </>
  )
}
