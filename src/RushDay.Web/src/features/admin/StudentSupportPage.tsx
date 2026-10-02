import { useId, useState, type ReactNode } from 'react'
import {
  BookPlus,
  Download,
  Eye,
  EyeOff,
  KeyRound,
  LogOut,
  Pencil,
  PenLine,
  SearchX,
} from 'lucide-react'
import { Link, useParams } from 'react-router'

import { studentExportPath } from '@/api/endpoints/admin'
import { describeProblem } from '@/api/problem'
import type { AdminStudentGrade, AdminStudentView } from '@/api/types/admin'
import {
  AmendedBadge,
  Badge,
  Button,
  ButtonLink,
  Card,
  CardDescription,
  CardHeader,
  CardTitle,
  EmptyState,
  ErrorState,
  LoadingRegion,
  PageHeader,
  Refetching,
  Skeleton,
  SkeletonText,
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeaderCell,
  TableRow,
} from '@/components/ui'
import { formatDateTime, formatDecimal, formatSemester, formatShortDate } from '@/lib/format'
import { downloadFromApi } from '@/lib/download'
import { toast } from '@/lib/toast'
import { useDocumentTitle } from '@/lib/useDocumentTitle'

import { AccountRowActions } from './components/AccountRowActions'
import { AuditTable } from './components/AuditRow'
import { CorrectMarkDialog, type CorrectionTarget } from './components/CorrectMarkDialog'
import { EditStudentDialog } from './components/EditStudentDialog'
import { MarkStudentLeftDialog } from './components/MarkStudentLeftDialog'
import { OverrideEnrolDialog } from './components/OverrideEnrolDialog'
import { OverrideWithdrawDialog, type WithdrawTarget } from './components/OverrideWithdrawDialog'
import { ProvisionAccountDialog } from './components/ProvisionAccountDialog'
import {
  AccountStateChips,
  EnrolmentStatusChip,
  GradeStatusChip,
  LeftBadge,
} from './components/StatusChips'
import { useStudent } from './hooks/useStudents'
import { describeMark } from './lib/marks'
import { useInstitutionClock } from './lib/useInstitutionClock'
import { currentTime } from '@/lib/zonedTime'

const STUDENT_NUMBER = /^S\d{6}$/

function Section({
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

function Fact({ label, children }: { label: string; children: ReactNode }) {
  return (
    <div className="flex flex-col gap-0.5">
      <dt className="text-sm text-muted">{label}</dt>
      <dd className="text-sm font-medium break-words text-text">{children}</dd>
    </div>
  )
}

function ExportButton({ studentNumber }: { studentNumber: string }) {
  const [busy, setBusy] = useState(false)
  async function exportData() {
    setBusy(true)
    try {
      await downloadFromApi(studentExportPath(studentNumber), `rushday-${studentNumber}.json`)
      toast.success(`Downloaded ${studentNumber}'s data. The export is recorded in the audit log.`)
    } catch (error) {
      toast.error(describeProblem(error).message)
    } finally {
      setBusy(false)
    }
  }
  return (
    <Button variant="secondary" loading={busy} onClick={() => void exportData()}>
      <Download aria-hidden="true" className="size-4" />
      Export data (JSON)<span className="sr-only">, downloads a file</span>
    </Button>
  )
}

function RecordCard({
  view,
  timeZone,
  activeThisYear,
}: {
  view: AdminStudentView
  timeZone: string
  activeThisYear: number
}) {
  const [editOpen, setEditOpen] = useState(false)
  const [leaveOpen, setLeaveOpen] = useState(false)
  const { student } = view
  return (
    <Section
      title="Record"
      actions={
        <>
          <Button variant="secondary" size="sm" onClick={() => setEditOpen(true)}>
            <Pencil aria-hidden="true" className="size-4" />
            Edit
          </Button>
          {!student.leftAt && (
            <Button variant="danger" size="sm" onClick={() => setLeaveOpen(true)}>
              <LogOut aria-hidden="true" className="size-4" />
              Mark as left
            </Button>
          )}
        </>
      }
    >
      <dl className="grid grid-cols-2 gap-4 sm:grid-cols-3">
        <Fact label="Student number">
          <span className="font-mono">{student.studentNumber}</span>
        </Fact>
        <Fact label="Name">{student.fullName}</Fact>
        <Fact label="Programme">{student.programme}</Fact>
        <Fact label="Year of study">{student.yearOfStudy}</Fact>
        <Fact label="Email">{student.email ?? 'Not recorded'}</Fact>
        <Fact label="Status">
          {student.leftAt ? (
            <span className="flex flex-wrap items-center gap-2">
              <LeftBadge leftAt={student.leftAt} timeZone={timeZone} />
              {formatShortDate(student.leftAt, timeZone)}
            </span>
          ) : (
            'Studying'
          )}
        </Fact>
      </dl>
      <EditStudentDialog student={student} open={editOpen} onOpenChange={setEditOpen} />
      <MarkStudentLeftDialog
        student={student}
        activeEnrolments={activeThisYear}
        open={leaveOpen}
        onOpenChange={setLeaveOpen}
      />
    </Section>
  )
}

function AccountCard({ view, timeZone }: { view: AdminStudentView; timeZone: string }) {
  const [provisionOpen, setProvisionOpen] = useState(false)
  const { account, student } = view
  if (!account) {
    return (
      <Section title="Account">
        <EmptyState
          compact
          icon={KeyRound}
          title="No account yet."
          description={`${student.fullName} can't sign in until an account is provisioned.`}
          action={
            student.leftAt ? undefined : (
              <Button onClick={() => setProvisionOpen(true)}>Provision account</Button>
            )
          }
        />
        <ProvisionAccountDialog
          open={provisionOpen}
          onOpenChange={setProvisionOpen}
          initialStudent={{ ...student, accountState: 'none' }}
        />
      </Section>
    )
  }
  return (
    <Section
      title="Account"
      actions={<AccountRowActions account={account} showViewStudent={false} />}
    >
      <dl className="grid grid-cols-2 gap-4">
        <Fact label="Username">
          <span className="font-mono">{account.username}</span>
        </Fact>
        <Fact label="Last sign-in">
          {account.lastLoginAt ? formatDateTime(account.lastLoginAt, timeZone) : 'Never'}
        </Fact>
        <div className="col-span-2 flex flex-col gap-1">
          <dt className="text-sm text-muted">State</dt>
          <dd>
            <AccountStateChips account={account} timeZone={timeZone} />
          </dd>
        </div>
      </dl>
    </Section>
  )
}

function EnrolmentsCard({
  view,
  timeZone,
  onEnrol,
  onWithdraw,
}: {
  view: AdminStudentView
  timeZone: string
  onEnrol: () => void
  onWithdraw: (target: WithdrawTarget) => void
}) {
  const withResults = new Set(
    view.grades
      .filter((grade) => grade.status !== 'draft')
      .map((grade) => `${grade.moduleCode}|${grade.academicYear}`),
  )
  return (
    <Section
      title="Enrolments"
      description="Every academic year, newest first."
      actions={
        !view.student.leftAt && (
          <Button variant="secondary" size="sm" onClick={onEnrol}>
            <BookPlus aria-hidden="true" className="size-4" />
            Enrol on a module
          </Button>
        )
      }
    >
      {view.enrolments.length === 0 ? (
        <p className="text-sm text-muted">No enrolments.</p>
      ) : (
        <Table caption={`Enrolments of ${view.student.studentNumber}`} captionHidden>
          <TableHead>
            <TableRow>
              <TableHeaderCell>Module</TableHeaderCell>
              <TableHeaderCell>Year</TableHeaderCell>
              <TableHeaderCell>Status</TableHeaderCell>
              <TableHeaderCell>Source</TableHeaderCell>
              <TableHeaderCell>Enrolled</TableHeaderCell>
              <TableHeaderCell>
                <span className="sr-only">Actions</span>
              </TableHeaderCell>
            </TableRow>
          </TableHead>
          <TableBody>
            {view.enrolments.map((enrolment) => (
              <TableRow key={`${enrolment.moduleCode}-${enrolment.academicYear}`}>
                <TableCell label="Module">
                  <span className="flex flex-col">
                    <Link
                      to={`/admin/modules/${enrolment.moduleCode}`}
                      className="font-mono text-primary underline-offset-2 hover:underline"
                    >
                      {enrolment.moduleCode}
                    </Link>
                    <span className="text-xs text-muted">
                      {enrolment.title} · {enrolment.credits} credits
                    </span>
                  </span>
                </TableCell>
                <TableCell label="Year">
                  {enrolment.academicYear} {formatSemester(enrolment.semester)}
                </TableCell>
                <TableCell label="Status">
                  <span className="flex flex-col gap-1">
                    <EnrolmentStatusChip status={enrolment.status} />
                    {enrolment.withdrawnAt && (
                      <span className="text-xs text-muted">
                        {formatShortDate(enrolment.withdrawnAt, timeZone)}
                      </span>
                    )}
                  </span>
                </TableCell>
                <TableCell label="Source">
                  {enrolment.source === 'admin'
                    ? 'Administrator'
                    : enrolment.source === 'self'
                      ? 'Self-service'
                      : 'Imported'}
                </TableCell>
                <TableCell label="Enrolled">
                  {formatShortDate(enrolment.enrolledAt, timeZone)}
                </TableCell>
                <TableCell className="text-right">
                  {enrolment.status === 'active' && (
                    <Button
                      variant="secondary"
                      size="sm"
                      aria-label={`Withdraw from ${enrolment.moduleCode}`}
                      onClick={() =>
                        onWithdraw({
                          code: enrolment.moduleCode,
                          title: enrolment.title,
                          academicYear: enrolment.academicYear,
                          hasResult: withResults.has(
                            `${enrolment.moduleCode}|${enrolment.academicYear}`,
                          ),
                        })
                      }
                    >
                      Withdraw
                    </Button>
                  )}
                </TableCell>
              </TableRow>
            ))}
          </TableBody>
        </Table>
      )}
    </Section>
  )
}

function GradesCard({
  view,
  timeZone,
  mountedAt,
  onCorrect,
}: {
  view: AdminStudentView
  timeZone: string
  mountedAt: number
  onCorrect: (target: CorrectionTarget) => void
}) {
  const scheduled = (grade: AdminStudentGrade) =>
    grade.status === 'published' &&
    grade.publishedAt !== null &&
    Date.parse(grade.publishedAt) > mountedAt

  return (
    <Section
      title="Marks"
      description="Every grade with its status. Students see only the rows marked Visible."
    >
      <div className="mb-4 rounded-md border border-border bg-surface-2 px-4 py-3 text-sm">
        {view.weightedAverage === null ? (
          <p className="text-muted">No published marks yet, so the student sees no average.</p>
        ) : (
          <p>
            <span className="text-muted">Average as the student sees it: </span>
            <span className="font-semibold tabular-nums">
              {formatDecimal(view.weightedAverage)}
            </span>
            {view.classification && (
              <span className="text-muted"> · indicative band {view.classification}</span>
            )}
          </p>
        )}
      </div>
      {view.grades.length === 0 ? (
        <p className="text-sm text-muted">No marks recorded.</p>
      ) : (
        <Table caption={`Marks of ${view.student.studentNumber}`} captionHidden>
          <TableHead>
            <TableRow>
              <TableHeaderCell>Module</TableHeaderCell>
              <TableHeaderCell>Year</TableHeaderCell>
              <TableHeaderCell numeric>Mark</TableHeaderCell>
              <TableHeaderCell>Status</TableHeaderCell>
              <TableHeaderCell>Student sees it</TableHeaderCell>
              <TableHeaderCell>
                <span className="sr-only">Actions</span>
              </TableHeaderCell>
            </TableRow>
          </TableHead>
          <TableBody>
            {view.grades.map((grade) => (
              <TableRow key={`${grade.moduleCode}-${grade.academicYear}`}>
                <TableCell label="Module">
                  <span className="flex flex-col">
                    <span className="font-mono">{grade.moduleCode}</span>
                    <span className="text-xs text-muted">{grade.moduleTitle}</span>
                  </span>
                </TableCell>
                <TableCell label="Year">
                  {grade.academicYear} {formatSemester(grade.semester)}
                </TableCell>
                <TableCell label="Mark" numeric>
                  {describeMark(grade)}
                </TableCell>
                <TableCell label="Status">
                  <span className="flex flex-wrap gap-1.5">
                    <GradeStatusChip status={grade.status} live={!scheduled(grade)} />
                    {grade.correctedAt && (
                      <AmendedBadge correctedAt={grade.correctedAt} timeZone={timeZone} />
                    )}
                  </span>
                  {scheduled(grade) && grade.publishedAt && (
                    <span className="mt-1 block text-xs text-muted">
                      {formatDateTime(grade.publishedAt, timeZone)}
                    </span>
                  )}
                </TableCell>
                <TableCell label="Student sees it">
                  {grade.visibleToStudent ? (
                    <Badge variant="success" icon={Eye}>
                      Visible
                    </Badge>
                  ) : (
                    <Badge variant="neutral" icon={EyeOff}>
                      Hidden
                    </Badge>
                  )}
                </TableCell>
                <TableCell className="text-right">
                  {grade.status !== 'draft' && (
                    <Button
                      variant="secondary"
                      size="sm"
                      aria-label={`Correct mark for ${grade.moduleCode}`}
                      onClick={() =>
                        onCorrect({
                          code: grade.moduleCode,
                          studentNumber: view.student.studentNumber,
                          studentName: view.student.fullName,
                          current: { outcome: grade.outcome, mark: grade.mark },
                          live: grade.visibleToStudent,
                        })
                      }
                    >
                      <PenLine aria-hidden="true" className="size-4" />
                      Correct mark
                    </Button>
                  )}
                </TableCell>
              </TableRow>
            ))}
          </TableBody>
        </Table>
      )}
    </Section>
  )
}

function PageSkeleton() {
  return (
    <LoadingRegion label="student record">
      <Skeleton className="mb-3 h-5 w-24" />
      <Skeleton className="mb-8 h-8 w-1/2" />
      <div className="grid gap-6 lg:grid-cols-2">
        {[0, 1].map((key) => (
          <div key={key} className="rounded-lg border border-border bg-surface p-6">
            <Skeleton className="mb-4 h-6 w-32" />
            <SkeletonText lines={4} />
          </div>
        ))}
      </div>
    </LoadingRegion>
  )
}

/**
 * `/admin/students/:studentNumber` (05-frontend.md section 10): the student as they see it plus
 * every status, with edit, mark as left, account actions, data export, override enrol and withdraw,
 * mark corrections and recent audit. Opening the page is itself recorded (`student.viewed`).
 */
export function Component() {
  const params = useParams()
  const studentNumber = (params.studentNumber ?? '').toUpperCase()
  const valid = STUDENT_NUMBER.test(studentNumber)
  useDocumentTitle(`${studentNumber} · Students · RushDay`)
  const { timeZone, academicYear } = useInstitutionClock()
  const query = useStudent(studentNumber, { enabled: valid })
  const [mountedAt] = useState(currentTime)
  const [enrolOpen, setEnrolOpen] = useState(false)
  const [withdrawTarget, setWithdrawTarget] = useState<WithdrawTarget | null>(null)
  const [correction, setCorrection] = useState<CorrectionTarget | null>(null)

  const banner = (
    <p className="mb-6 flex items-start gap-2 rounded-lg border border-info/30 bg-info-soft px-4 py-3 text-sm font-medium text-info">
      <Eye aria-hidden="true" className="mt-0.5 size-4 shrink-0" />
      Viewing as administrator. This view is recorded in the audit log.
    </p>
  )

  if (!valid) {
    return (
      <>
        <PageHeader title="Student not found" />
        <Card>
          <EmptyState
            icon={SearchX}
            title="That page or record doesn't exist."
            action={
              <ButtonLink to="/admin/students" variant="secondary">
                All students
              </ButtonLink>
            }
          />
        </Card>
      </>
    )
  }

  if (query.isPending) return <PageSkeleton />
  if (query.isError) {
    return (
      <>
        <PageHeader title={studentNumber} />
        <Card>
          <ErrorState
            error={query.error}
            onRetry={() => void query.refetch()}
            action={
              <ButtonLink to="/admin/students" variant="secondary">
                All students
              </ButtonLink>
            }
          />
        </Card>
      </>
    )
  }

  const view = query.data
  const { student } = view
  const activeThisYear = view.enrolments.filter(
    (enrolment) => enrolment.status === 'active' && enrolment.academicYear === academicYear,
  ).length

  return (
    <>
      <PageHeader
        eyebrow={<span className="font-mono">{student.studentNumber}</span>}
        title={student.fullName}
        description={`${student.programme}, year ${student.yearOfStudy}`}
        actions={<ExportButton studentNumber={student.studentNumber} />}
      />
      {banner}
      <Refetching active={query.isFetching}>
        <div className="flex flex-col gap-6">
          <div className="grid gap-6 lg:grid-cols-2">
            <RecordCard view={view} timeZone={timeZone} activeThisYear={activeThisYear} />
            <AccountCard view={view} timeZone={timeZone} />
          </div>
          <EnrolmentsCard
            view={view}
            timeZone={timeZone}
            onEnrol={() => setEnrolOpen(true)}
            onWithdraw={setWithdrawTarget}
          />
          <GradesCard
            view={view}
            timeZone={timeZone}
            mountedAt={mountedAt}
            onCorrect={setCorrection}
          />
          <Section
            title="Recent activity"
            description="The latest audit entries about this student."
          >
            {view.recentAudit.length === 0 ? (
              <p className="text-sm text-muted">Nothing recorded yet.</p>
            ) : (
              <AuditTable
                events={view.recentAudit}
                timeZone={timeZone}
                caption={`Recent activity for ${student.studentNumber}`}
              />
            )}
            <p className="mt-3 text-sm">
              <Link
                to={`/admin/audit?studentNumber=${student.studentNumber}`}
                className="font-medium text-primary underline-offset-2 hover:underline"
              >
                Full audit history for {student.studentNumber}
              </Link>
            </p>
          </Section>
        </div>
      </Refetching>
      <OverrideEnrolDialog
        studentNumber={student.studentNumber}
        studentName={student.fullName}
        open={enrolOpen}
        onOpenChange={setEnrolOpen}
      />
      <OverrideWithdrawDialog
        studentNumber={student.studentNumber}
        studentName={student.fullName}
        target={withdrawTarget}
        onOpenChange={(open) => !open && setWithdrawTarget(null)}
      />
      <CorrectMarkDialog
        target={correction}
        onOpenChange={(open) => !open && setCorrection(null)}
      />
    </>
  )
}
