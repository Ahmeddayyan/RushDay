import { useId, useState } from 'react'
import { ClipboardCheck, GraduationCap, History } from 'lucide-react'

import type { AdminResultsModule, PublishResponse } from '@/api/types/admin'
import type { PublicationInfo, Semester } from '@/api/types/common'
import {
  Button,
  Card,
  CardDescription,
  CardHeader,
  CardTitle,
  Checkbox,
  EmptyState,
  ErrorState,
  FormField,
  PageHeader,
  Refetching,
  Select,
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeaderCell,
  TableRow,
} from '@/components/ui'
import { formatDateTime, formatNumber, formatSemester } from '@/lib/format'
import { toast } from '@/lib/toast'
import { useDocumentTitle } from '@/lib/useDocumentTitle'

import { CancelPublicationDialog } from './components/CancelPublicationDialog'
import { PublishDialog } from './components/PublishDialog'
import { RescheduleDialog } from './components/RescheduleDialog'
import { ReturnToDraftDialog } from './components/ReturnToDraftDialog'
import { PublicationStateChip } from './components/StatusChips'
import { SubmissionProgressTable } from './components/SubmissionProgressTable'
import { TableSkeleton } from './components/TableSkeleton'
import { UnpublishDialog } from './components/UnpublishDialog'
import { useAdminResults } from './hooks/useResults'
import { useAdminSettings } from './hooks/useSettings'
import { publishPreview, recentAcademicYears, sortProgress } from './lib/results'
import { useInstitutionClock } from './lib/useInstitutionClock'
import { useUrlFilters } from './lib/urlState'

const FILTERS = ['year', 'semester', 'all'] as const

function countOf(n: number, one: string, many: string): string {
  return `${formatNumber(n)} ${n === 1 ? one : many}`
}

function focusHistoryRow(id: string) {
  const row = document.getElementById(`publication-${id}`)
  row?.scrollIntoView({ block: 'center' })
  row?.focus()
}

function HistoryTable({
  publications,
  timeZone,
  onReschedule,
  onCancel,
  onUnpublish,
}: {
  publications: PublicationInfo[]
  timeZone: string
  onReschedule: (publication: PublicationInfo) => void
  onCancel: (publication: PublicationInfo) => void
  onUnpublish: (publication: PublicationInfo) => void
}) {
  const headingId = useId()
  return (
    <section aria-labelledby={headingId} className="flex flex-col gap-3">
      <h2 id={headingId} className="flex items-center gap-2 text-lg font-semibold text-text">
        <History aria-hidden="true" className="size-5 text-muted" />
        Publication history
      </h2>
      {publications.length === 0 ? (
        <Card>
          <p className="text-sm text-muted">Nothing has been published for this semester yet.</p>
        </Card>
      ) : (
        <Table caption="Publication history" captionHidden>
          <TableHead>
            <TableRow>
              <TableHeaderCell>Students see them</TableHeaderCell>
              <TableHeaderCell>State</TableHeaderCell>
              <TableHeaderCell numeric>Modules</TableHeaderCell>
              <TableHeaderCell numeric>Marks</TableHeaderCell>
              <TableHeaderCell>Published by</TableHeaderCell>
              <TableHeaderCell>
                <span className="sr-only">Actions</span>
              </TableHeaderCell>
            </TableRow>
          </TableHead>
          <TableBody>
            {publications.map((publication) => (
              <TableRow
                key={publication.id}
                id={`publication-${publication.id}`}
                tabIndex={-1}
                className="outline-none focus-visible:bg-primary-soft"
              >
                <TableCell label="Students see them">
                  {formatDateTime(publication.publishAt, timeZone)}
                  {publication.note && (
                    <span className="block text-xs text-muted">{publication.note}</span>
                  )}
                </TableCell>
                <TableCell label="State">
                  <PublicationStateChip state={publication.state} />
                </TableCell>
                <TableCell label="Modules" numeric>
                  {formatNumber(publication.moduleCount)}
                </TableCell>
                <TableCell label="Marks" numeric>
                  {formatNumber(publication.gradeCount)}
                </TableCell>
                <TableCell label="Published by">
                  {publication.createdBy ?? 'System'}
                  <span className="block text-xs text-muted">
                    {formatDateTime(publication.createdAt, timeZone, { zone: false })}
                  </span>
                </TableCell>
                <TableCell className="text-right">
                  <span className="inline-flex flex-wrap justify-end gap-2">
                    {publication.state === 'scheduled' ? (
                      <>
                        <Button
                          variant="secondary"
                          size="sm"
                          onClick={() => onReschedule(publication)}
                        >
                          Reschedule
                        </Button>
                        <Button variant="secondary" size="sm" onClick={() => onCancel(publication)}>
                          Cancel
                        </Button>
                      </>
                    ) : (
                      <Button
                        variant="secondary"
                        size="sm"
                        onClick={() => onUnpublish(publication)}
                      >
                        Unpublish
                      </Button>
                    )}
                  </span>
                </TableCell>
              </TableRow>
            ))}
          </TableBody>
        </Table>
      )}
    </section>
  )
}

/**
 * `/admin/results` (05-frontend.md section 10): submission progress for a (year, semester), the
 * results-day `PublishDialog`, and the publication history with reschedule, cancel and unpublish.
 */
export function Component() {
  useDocumentTitle('Results · RushDay')
  const { timeZone, academicYear: statusYear, now } = useInstitutionClock()
  const settings = useAdminSettings()
  const { values, update } = useUrlFilters(FILTERS)
  const settingsYear = settings.data?.academicYear ?? statusYear ?? ''
  const academicYear = values.year || settingsYear
  const semester: Semester =
    values.semester === 'autumn' || values.semester === 'spring'
      ? values.semester
      : (settings.data?.currentSemester ?? 'autumn')
  const hideEmpty = values.all !== '1'
  const query = useAdminResults(academicYear, semester)

  const [publishOpenedAt, setPublishOpenedAt] = useState<number | null>(null)
  const [returning, setReturning] = useState<AdminResultsModule | null>(null)
  const [rescheduling, setRescheduling] = useState<{
    publication: PublicationInfo
    openedAt: number
  } | null>(null)
  const [cancelling, setCancelling] = useState<PublicationInfo | null>(null)
  const [unpublishing, setUnpublishing] = useState<PublicationInfo | null>(null)
  const progressHeadingId = useId()

  const target = `${formatSemester(semester)} ${academicYear}`
  const years = recentAcademicYears(settingsYear || academicYear)
  if (academicYear && !years.includes(academicYear)) years.unshift(academicYear)

  function onPublished(result: PublishResponse, publishedNow: boolean) {
    setPublishOpenedAt(null)
    const when = publishedNow
      ? 'now'
      : `at ${formatDateTime(result.publication.publishAt, timeZone)}`
    toast.success(
      `Published: ${countOf(result.published.modules, 'module', 'modules')}, ${countOf(result.published.grades, 'mark', 'marks')}. Students see them ${when}.`,
      {
        action: {
          label: 'View in history',
          onClick: () => focusHistoryRow(result.publication.id),
        },
      },
    )
  }

  const pickers = (
    <div className="mb-6 flex flex-wrap items-end gap-3">
      <FormField label="Academic year" className="w-full sm:w-40">
        <Select
          value={academicYear}
          onChange={(event) =>
            update({ year: event.target.value === settingsYear ? null : event.target.value })
          }
          options={years.map((year) => ({ value: year, label: year }))}
        />
      </FormField>
      <FormField label="Semester" className="w-full sm:w-40">
        <Select
          value={semester}
          onChange={(event) => update({ semester: event.target.value })}
          options={[
            { value: 'autumn', label: 'Autumn' },
            { value: 'spring', label: 'Spring' },
          ]}
        />
      </FormField>
      <Checkbox
        label="Hide modules without students"
        checked={hideEmpty}
        onChange={(event) => update({ all: event.target.checked ? null : '1' })}
        className="sm:pb-2"
      />
    </div>
  )

  let content
  if (query.isPending) {
    content = <TableSkeleton label="submission progress" rows={8} />
  } else if (query.isError) {
    content = (
      <Card>
        <ErrorState error={query.error} onRetry={() => void query.refetch()} />
      </Card>
    )
  } else {
    const data = query.data
    const preview = publishPreview(data.modules)
    const shown = sortProgress(
      hideEmpty
        ? data.modules.filter((module) => module.marks.status !== 'noStudents')
        : data.modules,
    )
    const anySubmitted = data.modules.some((module) =>
      ['submitted', 'scheduled', 'published'].includes(module.marks.status),
    )
    content = (
      <Refetching active={query.isFetching}>
        <div className="flex flex-col gap-8">
          <Card>
            <CardHeader
              actions={
                <Button
                  onClick={() => setPublishOpenedAt(now())}
                  disabled={preview.modules.length === 0}
                >
                  <GraduationCap aria-hidden="true" className="size-4" />
                  Publish results
                </Button>
              }
            >
              <CardTitle id={progressHeadingId}>Submission progress</CardTitle>
              <CardDescription>
                {preview.modules.length > 0
                  ? `${formatNumber(preview.modules.length)} modules (${formatNumber(preview.grades)} marks) are ready to publish for ${target}.`
                  : `No submitted modules are ready to publish for ${target}. Lecturers submit modules from their Marks page.`}
              </CardDescription>
            </CardHeader>
            {!anySubmitted && shown.length > 0 && (
              <p className="mb-4 text-sm text-muted">
                Nothing submitted for {target} yet; lecturers submit from their Marks page.
              </p>
            )}
            {shown.length === 0 ? (
              <EmptyState
                compact
                icon={ClipboardCheck}
                title={`Nothing submitted for ${target} yet; lecturers submit from their Marks page.`}
              />
            ) : (
              <SubmissionProgressTable
                modules={shown}
                timeZone={timeZone}
                caption={`Submission progress for ${target}`}
                onReturnToDraft={setReturning}
              />
            )}
          </Card>
          <HistoryTable
            publications={data.publications}
            timeZone={timeZone}
            onReschedule={(publication) => setRescheduling({ publication, openedAt: now() })}
            onCancel={setCancelling}
            onUnpublish={setUnpublishing}
          />
        </div>
        {publishOpenedAt !== null && (
          <PublishDialog
            open
            onOpenChange={(open) => !open && setPublishOpenedAt(null)}
            academicYear={data.academicYear}
            semester={data.semester}
            modules={data.modules}
            timeZone={timeZone}
            openedAt={publishOpenedAt}
            onPublished={onPublished}
          />
        )}
      </Refetching>
    )
  }

  return (
    <>
      <PageHeader
        title="Results"
        description="Check submissions, publish at the results-day instant, and correct afterwards."
      />
      {pickers}
      {content}
      <ReturnToDraftDialog
        module={returning}
        academicYear={academicYear}
        timeZone={timeZone}
        onOpenChange={(open) => !open && setReturning(null)}
      />
      <RescheduleDialog
        publication={rescheduling?.publication ?? null}
        openedAt={rescheduling?.openedAt ?? 0}
        timeZone={timeZone}
        onOpenChange={(open) => !open && setRescheduling(null)}
      />
      <CancelPublicationDialog
        publication={cancelling}
        timeZone={timeZone}
        onOpenChange={(open) => !open && setCancelling(null)}
      />
      <UnpublishDialog
        publication={unpublishing}
        onOpenChange={(open) => !open && setUnpublishing(null)}
      />
    </>
  )
}
