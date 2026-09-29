import { CalendarClock, CircleCheck, Lock, type LucideIcon } from 'lucide-react'

import type { Semester, WindowInfo } from '@/api/types/common'
import type { CreditBudgets } from '@/api/types/student'
import { Badge, ButtonLink, Card, CardHeader, CardTitle, type BadgeVariant } from '@/components/ui'
import { formatDateTime, formatSemester } from '@/lib/format'

import { CreditBudget } from './CreditBudget'

export interface EnrolmentWindowCardProps {
  windows: readonly WindowInfo[]
  credits: CreditBudgets
  academicYear: string
  timeZone: string
}

const SEMESTERS: readonly Semester[] = ['autumn', 'spring']

interface WindowView {
  label: string
  variant: BadgeVariant
  icon: LucideIcon
  sentence: string
}

function describeWindow(window: WindowInfo | undefined, timeZone: string): WindowView {
  if (!window) {
    return {
      label: 'Not announced',
      variant: 'neutral',
      icon: CalendarClock,
      sentence: 'Enrolment dates have not been announced yet.',
    }
  }
  switch (window.state) {
    case 'open':
      return {
        label: 'Open',
        variant: 'success',
        icon: CircleCheck,
        sentence: `Open until ${formatDateTime(window.closesAt, timeZone)}.`,
      }
    case 'notYetOpen':
      return {
        label: 'Not open yet',
        variant: 'info',
        icon: CalendarClock,
        sentence: `Opens ${formatDateTime(window.opensAt, timeZone)}.`,
      }
    case 'closed':
      return {
        label: 'Closed',
        variant: 'neutral',
        icon: Lock,
        sentence: `Closed on ${formatDateTime(window.closesAt, timeZone)}.`,
      }
  }
}

/**
 * Enrolment (05-frontend.md section 10, `/student`): the window state of each semester of the
 * current year with its credit budget ("15 of 60 credits, Autumn 2026/27"), and "Browse modules".
 */
export function EnrolmentWindowCard({
  windows,
  credits,
  academicYear,
  timeZone,
}: EnrolmentWindowCardProps) {
  return (
    <Card>
      <CardHeader
        actions={
          <ButtonLink to="/student/modules" variant="secondary" size="sm">
            Browse modules
          </ButtonLink>
        }
      >
        <CardTitle>Enrolment</CardTitle>
        <p className="text-sm text-muted">{academicYear}</p>
      </CardHeader>
      <ul className="flex flex-col gap-5">
        {SEMESTERS.map((semester) => {
          const window = windows.find(
            (item) => item.semester === semester && item.academicYear === academicYear,
          )
          const view = describeWindow(window, timeZone)
          return (
            <li key={semester} className="flex flex-col gap-2.5">
              <div className="flex flex-wrap items-center justify-between gap-2">
                <h3 className="text-sm font-semibold text-text">{formatSemester(semester)}</h3>
                <Badge variant={view.variant} icon={view.icon}>
                  {view.label}
                </Badge>
              </div>
              <p className="text-sm text-muted">{view.sentence}</p>
              <CreditBudget
                semester={semester}
                used={credits[semester]}
                limit={credits.limit}
                academicYear={academicYear}
              />
            </li>
          )
        })}
      </ul>
    </Card>
  )
}
