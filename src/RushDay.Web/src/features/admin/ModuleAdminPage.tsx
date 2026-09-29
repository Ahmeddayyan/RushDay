import { useId, useState, type ReactNode } from 'react'
import { CircleSlash, Pencil, SearchX, Scissors, TriangleAlert } from 'lucide-react'
import { useLocation, useParams } from 'react-router'

import type { AdminModule } from '@/api/types/admin'
import {
  Badge,
  Button,
  ButtonLink,
  Card,
  CardHeader,
  CardTitle,
  EmptyState,
  ErrorState,
  LoadingRegion,
  MarksStatusChip,
  PageHeader,
  Skeleton,
  SkeletonText,
  TabNav,
} from '@/components/ui'
import { formatDateTime, formatNumber, formatSemester } from '@/lib/format'
import { isModuleCode } from '@/lib/moduleCode'
import { useDocumentTitle } from '@/lib/useDocumentTitle'

import { LecturerAssignmentEditor } from './components/LecturerAssignmentEditor'
import { ModuleEditDialog } from './components/ModuleEditDialog'
import { ModuleMarksTable } from './components/ModuleMarksTable'
import { ModuleRosterTable } from './components/ModuleRosterTable'
import { TrimToCapacityDialog } from './components/TrimToCapacityDialog'
import { useAdminModules, useModuleDetail } from './hooks/useModules'
import { useInstitutionClock } from './lib/useInstitutionClock'

const DAYS: Record<string, string> = {
  monday: 'Monday',
  tuesday: 'Tuesday',
  wednesday: 'Wednesday',
  thursday: 'Thursday',
  friday: 'Friday',
  saturday: 'Saturday',
  sunday: 'Sunday',
}

function Fact({ label, children }: { label: string; children: ReactNode }) {
  return (
    <div className="flex flex-col gap-0.5">
      <dt className="text-sm text-muted">{label}</dt>
      <dd className="text-sm font-medium text-text tabular-nums">{children}</dd>
    </div>
  )
}

function TimetableCard({ code }: { code: string }) {
  const detail = useModuleDetail(code)
  const headingId = useId()
  return (
    <Card>
      <section aria-labelledby={headingId}>
        <CardHeader>
          <CardTitle id={headingId}>Timetable</CardTitle>
        </CardHeader>
        {detail.isPending ? (
          <LoadingRegion label="timetable">
            <SkeletonText lines={2} />
          </LoadingRegion>
        ) : detail.isError ? (
          <ErrorState error={detail.error} compact onRetry={() => void detail.refetch()} />
        ) : detail.data.timetable.length === 0 ? (
          <p className="text-sm text-muted">No timetable slots.</p>
        ) : (
          <ul className="flex flex-col gap-2 text-sm">
            {detail.data.timetable.map((slot) => (
              <li
                key={`${slot.day}-${slot.startTime}-${slot.room}`}
                className="flex flex-wrap gap-x-3"
              >
                <span className="font-medium">{DAYS[slot.day] ?? slot.day}</span>
                <span className="tabular-nums">
                  {slot.startTime}–{slot.endTime}
                </span>
                <span>{slot.kind === 'lab' ? 'Lab' : 'Lecture'}</span>
                <span className="text-muted">{slot.room}</span>
              </li>
            ))}
          </ul>
        )}
      </section>
    </Card>
  )
}

function DetailsTab({ module, timeZone }: { module: AdminModule; timeZone: string }) {
  const [editOpen, setEditOpen] = useState(false)
  const [trimOpen, setTrimOpen] = useState(false)
  const headingId = useId()
  const over = module.enrolledCount - module.capacity

  return (
    <div className="flex flex-col gap-6">
      {over > 0 && (
        <div
          role="status"
          className="flex flex-col gap-3 rounded-lg border border-warning/30 bg-warning-soft px-4 py-3 text-sm text-warning sm:flex-row sm:items-center sm:justify-between"
        >
          <p className="flex items-start gap-2 font-medium">
            <TriangleAlert aria-hidden="true" className="mt-0.5 size-4 shrink-0" />
            {module.code} has {formatNumber(module.enrolledCount)} students for{' '}
            {formatNumber(module.capacity)} places: {formatNumber(over)} over capacity.
          </p>
          <Button variant="secondary" size="sm" onClick={() => setTrimOpen(true)}>
            <Scissors aria-hidden="true" className="size-4" />
            Trim to capacity
          </Button>
        </div>
      )}
      <div className="grid gap-6 lg:grid-cols-2">
        <Card>
          <section aria-labelledby={headingId}>
            <CardHeader
              actions={
                <Button variant="secondary" size="sm" onClick={() => setEditOpen(true)}>
                  <Pencil aria-hidden="true" className="size-4" />
                  Edit
                </Button>
              }
            >
              <CardTitle id={headingId}>Details</CardTitle>
            </CardHeader>
            <dl className="grid grid-cols-2 gap-4 sm:grid-cols-3">
              <Fact label="Semester">{formatSemester(module.semester)}</Fact>
              <Fact label="Credits">{module.credits}</Fact>
              <Fact label="Level">{module.level}</Fact>
              <Fact label="Capacity">{formatNumber(module.capacity)}</Fact>
              <Fact label="Enrolled this year">{formatNumber(module.enrolledCount)}</Fact>
              <Fact label="Places left">{formatNumber(module.placesRemaining)}</Fact>
            </dl>
            <div className="mt-4 border-t border-border pt-4">
              <h3 className="mb-1 text-sm font-medium text-muted">Description</h3>
              <p className="text-sm whitespace-pre-line text-text">
                {module.description ?? 'No description.'}
              </p>
            </div>
            <div className="mt-4 flex flex-wrap items-center gap-2 border-t border-border pt-4 text-sm">
              <span className="text-muted">Marks this year:</span>
              <MarksStatusChip
                status={module.marks.status}
                publishedAt={module.marks.publishedAt}
                timeZone={timeZone}
              />
              <span className="text-muted tabular-nums">
                {formatNumber(module.marks.entered)} entered, {formatNumber(module.marks.missing)}{' '}
                missing
              </span>
              {module.marks.submittedAt && (
                <span className="text-muted">
                  Submitted {formatDateTime(module.marks.submittedAt, timeZone)}
                </span>
              )}
            </div>
          </section>
        </Card>
        <div className="flex flex-col gap-6">
          <LecturerAssignmentEditor
            key={module.lecturers
              .map((lecturer) => `${lecturer.staffNumber}:${lecturer.role}`)
              .join()}
            code={module.code}
            lecturers={module.lecturers}
          />
          <TimetableCard code={module.code} />
        </div>
      </div>
      <ModuleEditDialog open={editOpen} onOpenChange={setEditOpen} module={module} />
      <TrimToCapacityDialog
        code={module.code}
        capacity={module.capacity}
        enrolledCount={module.enrolledCount}
        open={trimOpen}
        onOpenChange={setTrimOpen}
      />
    </div>
  )
}

/**
 * `/admin/modules/:code` with tabs Details | Roster | Marks (`/roster`, `/marks`), 05-frontend.md
 * section 10. Each tab is a URL, so a roster page or a search can be linked.
 */
export function Component() {
  const params = useParams()
  const location = useLocation()
  const code = (params.code ?? '').toUpperCase()
  const valid = isModuleCode(code)
  const tab = location.pathname.endsWith('/roster')
    ? 'roster'
    : location.pathname.endsWith('/marks')
      ? 'marks'
      : 'details'
  const tabTitle = tab === 'roster' ? 'Roster' : tab === 'marks' ? 'Marks' : 'Details'
  useDocumentTitle(`${code} ${tabTitle} · RushDay`)
  const { timeZone } = useInstitutionClock()
  const modules = useAdminModules(true)
  const module = modules.data?.find((item) => item.code === code)

  if (!valid || (modules.isSuccess && !module)) {
    return (
      <>
        <PageHeader title="Module not found" />
        <Card>
          <EmptyState
            icon={SearchX}
            title="That page or record doesn't exist."
            description={`There is no module ${params.code ?? ''}.`}
            action={
              <ButtonLink to="/admin/modules" variant="secondary">
                All modules
              </ButtonLink>
            }
          />
        </Card>
      </>
    )
  }

  if (modules.isError) {
    return (
      <>
        <PageHeader title={code} />
        <Card>
          <ErrorState error={modules.error} onRetry={() => void modules.refetch()} />
        </Card>
      </>
    )
  }

  if (!module) {
    return (
      <LoadingRegion label={`module ${code}`}>
        <Skeleton className="mb-3 h-5 w-24" />
        <Skeleton className="mb-8 h-8 w-2/3" />
        <Skeleton className="h-64 w-full" />
      </LoadingRegion>
    )
  }

  const base = `/admin/modules/${module.code}`
  return (
    <>
      <PageHeader eyebrow={<span className="font-mono">{module.code}</span>} title={module.title}>
        <div className="flex flex-wrap gap-2 pt-1">
          <Badge variant="neutral">{formatSemester(module.semester)}</Badge>
          <Badge variant="neutral">{module.credits} credits</Badge>
          {!module.isActive && (
            <Badge variant="neutral" icon={CircleSlash}>
              Inactive
            </Badge>
          )}
          <MarksStatusChip
            status={module.marks.status}
            publishedAt={module.marks.publishedAt}
            timeZone={timeZone}
          />
        </div>
      </PageHeader>
      <TabNav
        label={`${module.code} sections`}
        className="mb-6"
        items={[
          { to: base, label: 'Details', end: true },
          { to: `${base}/roster`, label: 'Roster' },
          { to: `${base}/marks`, label: 'Marks' },
        ]}
      />
      {tab === 'details' && <DetailsTab module={module} timeZone={timeZone} />}
      {tab === 'roster' && <ModuleRosterTable code={module.code} timeZone={timeZone} />}
      {tab === 'marks' && <ModuleMarksTable code={module.code} timeZone={timeZone} />}
    </>
  )
}
