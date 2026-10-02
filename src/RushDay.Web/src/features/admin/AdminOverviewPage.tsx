import { useId, type ReactNode } from 'react'
import {
  BookOpen,
  CalendarClock,
  Database,
  GraduationCap,
  IdCard,
  KeyRound,
  Lock,
  Megaphone,
  UserPlus,
  UsersRound,
} from 'lucide-react'

import type { AdminOverview } from '@/api/types/admin'
import {
  Badge,
  ButtonLink,
  Card,
  CardDescription,
  CardHeader,
  CardTitle,
  ErrorState,
  LoadingRegion,
  Meter,
  PageHeader,
  Skeleton,
  StatTile,
} from '@/components/ui'
import { formatDateTime, formatNumber, formatSemester } from '@/lib/format'
import { useDocumentTitle } from '@/lib/useDocumentTitle'

import { AuditTable } from './components/AuditRow'
import { WindowStateChip } from './components/StatusChips'
import { useAdminOverview } from './hooks/useOverview'
import { useInstitutionClock } from './lib/useInstitutionClock'

function Panel({
  title,
  description,
  actions,
  children,
}: {
  title: string
  description?: ReactNode
  actions?: ReactNode
  children: ReactNode
}) {
  const headingId = useId()
  return (
    <Card>
      <section aria-labelledby={headingId}>
        <CardHeader actions={actions}>
          <CardTitle id={headingId}>{title}</CardTitle>
          {description && <CardDescription>{description}</CardDescription>}
        </CardHeader>
        {children}
      </section>
    </Card>
  )
}

function ResultsPanel({ overview, timeZone }: { overview: AdminOverview; timeZone: string }) {
  return (
    <Panel
      title="Results"
      description={`Submission progress for ${overview.academicYear}.`}
      actions={
        <ButtonLink to="/admin/results" size="sm">
          <GraduationCap aria-hidden="true" className="size-4" />
          Publish results
        </ButtonLink>
      }
    >
      <div className="flex flex-col gap-4">
        {overview.submissionProgress.map((progress) => {
          const done = progress.submitted + progress.scheduled + progress.published
          const semester = formatSemester(progress.semester)
          return (
            <div key={progress.semester} className="flex flex-col gap-1.5">
              <p className="text-sm font-medium text-text">
                Results: {formatNumber(done)} of {formatNumber(progress.modulesTotal)} modules
                submitted for {semester}
              </p>
              <Meter
                value={done}
                max={progress.modulesTotal}
                label={`Modules submitted for ${semester}`}
                valueText={`${formatNumber(progress.draft)} in draft, ${formatNumber(progress.submitted)} submitted, ${formatNumber(progress.scheduled)} scheduled, ${formatNumber(progress.published)} published`}
              />
            </div>
          )
        })}
        {overview.nextPublication && (
          <p className="flex items-center gap-2 text-sm">
            <CalendarClock aria-hidden="true" className="size-4 text-warning" />
            {formatSemester(overview.nextPublication.semester)}{' '}
            {overview.nextPublication.academicYear} results publish{' '}
            {formatDateTime(overview.nextPublication.publishAt, timeZone)}
          </p>
        )}
        {overview.latestPublication && (
          <p className="text-sm text-muted">
            Latest: {formatSemester(overview.latestPublication.semester)}{' '}
            {overview.latestPublication.academicYear} published{' '}
            {formatDateTime(overview.latestPublication.publishAt, timeZone)} (
            {formatNumber(overview.latestPublication.gradeCount)} marks)
          </p>
        )}
      </div>
    </Panel>
  )
}

function OverviewContent({ overview, timeZone }: { overview: AdminOverview; timeZone: string }) {
  const { counts } = overview
  return (
    <div className="flex flex-col gap-6">
      <div className="grid grid-cols-2 gap-4 md:grid-cols-3 xl:grid-cols-6">
        <StatTile label="Students" value={formatNumber(counts.students)} icon={UsersRound} />
        <StatTile label="Lecturers" value={formatNumber(counts.lecturers)} icon={IdCard} />
        <StatTile label="Modules" value={formatNumber(counts.modules)} icon={BookOpen} />
        <StatTile
          label="Active enrolments"
          value={formatNumber(counts.activeEnrolments)}
          detail={overview.academicYear}
          icon={GraduationCap}
        />
        <StatTile
          label="Accounts"
          value={formatNumber(counts.accounts)}
          detail={`${formatNumber(counts.disabledAccounts)} disabled`}
          icon={KeyRound}
        />
        <StatTile
          label="Locked accounts"
          value={formatNumber(counts.lockedAccounts)}
          icon={Lock}
          tone={counts.lockedAccounts > 0 ? 'warning' : 'default'}
        />
      </div>

      <div className="grid gap-6 lg:grid-cols-2">
        <Panel
          title="Enrolment windows"
          description={overview.academicYear}
          actions={
            <ButtonLink to="/admin/enrolment" variant="secondary" size="sm">
              Manage windows
            </ButtonLink>
          }
        >
          {overview.enrolmentWindows.length === 0 ? (
            <p className="text-sm text-muted">
              No windows for {overview.academicYear} yet, so students can&apos;t enrol themselves.
            </p>
          ) : (
            <ul className="flex flex-col gap-3">
              {overview.enrolmentWindows.map((window) => (
                <li key={window.id} className="flex flex-wrap items-center gap-x-3 gap-y-1 text-sm">
                  <span className="font-medium">{formatSemester(window.semester)}</span>
                  <WindowStateChip state={window.state} />
                  <span className="text-muted">
                    {window.state === 'notYetOpen'
                      ? `Opens ${formatDateTime(window.opensAt, timeZone)}`
                      : window.state === 'open'
                        ? `Closes ${formatDateTime(window.closesAt, timeZone)}`
                        : `Closed ${formatDateTime(window.closesAt, timeZone)}`}
                  </span>
                </li>
              ))}
            </ul>
          )}
        </Panel>
        <ResultsPanel overview={overview} timeZone={timeZone} />
      </div>

      <div className="grid gap-6 lg:grid-cols-[2fr_1fr]">
        <Panel title="Quick actions">
          <div className="flex flex-wrap gap-2">
            <ButtonLink to="/admin/accounts?provision=1" variant="secondary">
              <KeyRound aria-hidden="true" className="size-4" />
              Provision account
            </ButtonLink>
            <ButtonLink to="/admin/students?create=1" variant="secondary">
              <UserPlus aria-hidden="true" className="size-4" />
              Create student
            </ButtonLink>
            <ButtonLink to="/admin/results" variant="secondary">
              <GraduationCap aria-hidden="true" className="size-4" />
              Publish results
            </ButtonLink>
            <ButtonLink to="/admin/announcements?new=1" variant="secondary">
              <Megaphone aria-hidden="true" className="size-4" />
              New announcement
            </ButtonLink>
          </div>
        </Panel>
        <Panel title="Database">
          <p role="status" className="flex items-center gap-2 text-sm">
            <Database aria-hidden="true" className="size-4 text-muted" />
            {overview.database === 'ok' ? (
              <Badge variant="success">Database answering normally</Badge>
            ) : (
              <Badge variant="danger">Database degraded: check the Operations page</Badge>
            )}
          </p>
        </Panel>
      </div>

      <Panel
        title="Recent activity"
        actions={
          <ButtonLink to="/admin/audit" variant="secondary" size="sm">
            Audit log
          </ButtonLink>
        }
      >
        {overview.recentAudit.length === 0 ? (
          <p className="text-sm text-muted">Nothing recorded yet.</p>
        ) : (
          <AuditTable events={overview.recentAudit} timeZone={timeZone} caption="Recent activity" />
        )}
      </Panel>
    </div>
  )
}

/** `/admin` (05-frontend.md section 10): the registry's first screen, refreshed every 30 s. */
export function Component() {
  useDocumentTitle('Overview · Administration · RushDay')
  const { timeZone } = useInstitutionClock()
  const query = useAdminOverview()

  let content
  if (query.isPending) {
    content = (
      <LoadingRegion label="overview">
        <div className="grid grid-cols-2 gap-4 md:grid-cols-3 xl:grid-cols-6">
          {[0, 1, 2, 3, 4, 5].map((key) => (
            <Skeleton key={key} className="h-24 rounded-lg" />
          ))}
        </div>
        <div className="mt-6 grid gap-6 lg:grid-cols-2">
          <Skeleton className="h-48 rounded-lg" />
          <Skeleton className="h-48 rounded-lg" />
        </div>
      </LoadingRegion>
    )
  } else if (query.isError) {
    content = (
      <Card>
        <ErrorState error={query.error} onRetry={() => void query.refetch()} />
      </Card>
    )
  } else {
    // The 30-second poll updates in place: dimming the page every 30 s would only distract.
    content = <OverviewContent overview={query.data} timeZone={timeZone} />
  }

  return (
    <>
      <PageHeader
        title="Overview"
        description={
          query.data ? `Academic year ${query.data.academicYear}.` : 'The registry at a glance.'
        }
      />
      {content}
    </>
  )
}
