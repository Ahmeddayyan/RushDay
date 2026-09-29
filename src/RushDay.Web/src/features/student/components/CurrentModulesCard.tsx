import { BookOpen } from 'lucide-react'
import { Link } from 'react-router'

import type { DashboardModule } from '@/api/types/student'
import { ButtonLink, Card, CardHeader, CardTitle, EmptyState } from '@/components/ui'
import { formatSemester } from '@/lib/format'

export interface CurrentModulesCardProps {
  modules: readonly DashboardModule[]
  academicYear: string
}

/**
 * "This year's modules" (05-frontend.md section 10, `/student`): the current year's active
 * enrolments with credits and semester, each linking to its page.
 */
export function CurrentModulesCard({ modules, academicYear }: CurrentModulesCardProps) {
  const sorted = [...modules].sort(
    (a, b) => a.semester.localeCompare(b.semester) || a.code.localeCompare(b.code),
  )

  return (
    <Card>
      <CardHeader>
        <CardTitle>This year&apos;s modules</CardTitle>
        <p className="text-sm text-muted">{academicYear}</p>
      </CardHeader>
      {sorted.length === 0 ? (
        <EmptyState
          compact
          icon={BookOpen}
          headingLevel="p"
          title="You're not enrolled on any modules yet."
          action={
            <ButtonLink to="/student/modules" variant="secondary" size="sm">
              Browse modules
            </ButtonLink>
          }
        />
      ) : (
        <ul className="divide-y divide-border">
          {sorted.map((module) => (
            <li
              key={module.code}
              className="flex flex-col gap-1 py-2.5 first:pt-0 last:pb-0 sm:flex-row sm:items-baseline sm:justify-between sm:gap-4"
            >
              <Link
                to={`/student/modules/${module.code}`}
                className="min-w-0 rounded-sm font-medium text-text hover:text-primary hover:underline"
              >
                <span className="font-mono text-sm">{module.code}</span> {module.title}
              </Link>
              <span className="shrink-0 text-sm text-muted tabular-nums">
                {module.credits} credits · {formatSemester(module.semester)}
              </span>
            </li>
          ))}
        </ul>
      )}
    </Card>
  )
}
