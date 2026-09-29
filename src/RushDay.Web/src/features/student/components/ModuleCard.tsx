import { Link } from 'react-router'

import type { ModuleSummary, MyEnrolment } from '@/api/types/common'
import type { CompletedModule } from '@/api/types/student'
import { Card } from '@/components/ui'
import { formatSemester } from '@/lib/format'

import { lecturerName } from '../copy'
import { useEnrol } from '../hooks/useEnrol'
import { useWithdraw } from '../hooks/useWithdraw'

import { CapacityMeter } from './CapacityMeter'
import { EnrolButton } from './EnrolButton'

export interface ModuleCardProps {
  module: ModuleSummary
  row: MyEnrolment | undefined
  currentYear: string | null | undefined
  creditsUsed: number
  enrolmentsFresh: boolean
  completed: CompletedModule | null | undefined
  timeZone: string
}

/**
 * One catalogue card (05-frontend.md section 10, `/student/modules`): code and title (a link to the
 * module page), credits and semester, the leader, places and the enrol control.
 */
export function ModuleCard({
  module,
  row,
  currentYear,
  creditsUsed,
  enrolmentsFresh,
  completed,
  timeZone,
}: ModuleCardProps) {
  const control = useEnrol(module.code)
  const withdrawal = useWithdraw(module.code)
  const leader = module.lecturers.find((lecturer) => lecturer.role === 'leader')

  return (
    <Card className="flex h-full flex-col gap-4">
      <div className="min-w-0 space-y-1">
        <h2 className="text-lg leading-snug font-semibold tracking-tight">
          <Link
            to={`/student/modules/${module.code}`}
            className="rounded-sm text-text hover:text-primary hover:underline"
          >
            <span className="block font-mono text-sm font-medium text-muted">{module.code}</span>
            {module.title}
          </Link>
        </h2>
        <p className="text-sm text-muted">
          {module.credits} credits · {formatSemester(module.semester)} · Level {module.level}
        </p>
        <p className="text-sm text-muted">
          {leader ? `Led by ${lecturerName(leader)}` : 'Module leader to be confirmed'}
        </p>
      </div>
      <CapacityMeter module={module} filledAt={control.filledAt} />
      <EnrolButton
        className="mt-auto"
        module={module}
        row={row}
        currentYear={currentYear}
        creditsUsed={creditsUsed}
        enrolmentsFresh={enrolmentsFresh}
        control={control}
        onWithdraw={() => withdrawal.mutate()}
        completed={completed}
        timeZone={timeZone}
      />
    </Card>
  )
}
