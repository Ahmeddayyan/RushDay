import { History } from 'lucide-react'
import { Link } from 'react-router'

import type { CompletedModule } from '@/api/types/student'
import { Card, CardHeader, CardTitle, EmptyState } from '@/components/ui'
import { cn } from '@/lib/cn'
import { formatSemester } from '@/lib/format'

import { completedResultText } from '../grades'

export interface CompletedModulesCardProps {
  completed: readonly CompletedModule[]
}

/** Newest academic year first ("2025/26" sorts after "2024/25"). */
function byYear(completed: readonly CompletedModule[]): [string, CompletedModule[]][] {
  const groups = new Map<string, CompletedModule[]>()
  for (const module of completed) {
    const list = groups.get(module.academicYear) ?? []
    list.push(module)
    groups.set(module.academicYear, list)
  }
  return [...groups.entries()]
    .sort(([a], [b]) => b.localeCompare(a))
    .map(([year, list]) => [year, [...list].sort((a, b) => a.code.localeCompare(b.code))])
}

/**
 * "Completed modules" (05-frontend.md section 10, `/student`): earlier years' modules grouped by
 * academic year, each with "Mark {n} ({band})", "Absent" or "Deferred" when visible, else "Result
 * not yet published".
 */
export function CompletedModulesCard({ completed }: CompletedModulesCardProps) {
  return (
    <Card>
      <CardHeader>
        <CardTitle>Completed modules</CardTitle>
      </CardHeader>
      {completed.length === 0 ? (
        <EmptyState
          compact
          icon={History}
          headingLevel="p"
          title="No completed modules yet."
          description="Modules from earlier academic years appear here with their marks."
        />
      ) : (
        <div className="flex flex-col gap-5">
          {byYear(completed).map(([year, modules]) => (
            <section key={year} aria-labelledby={`completed-${year.replace('/', '-')}`}>
              <h3
                id={`completed-${year.replace('/', '-')}`}
                className="mb-2 text-sm font-semibold text-muted"
              >
                {year}
              </h3>
              <ul className="divide-y divide-border">
                {modules.map((module) => {
                  const result = completedResultText(module)
                  const unpublished = module.outcome === null
                  return (
                    <li
                      key={module.code}
                      className="flex flex-col gap-1 py-2.5 first:pt-0 last:pb-0 sm:flex-row sm:items-baseline sm:justify-between sm:gap-4"
                    >
                      <div className="min-w-0">
                        <Link
                          to={`/student/modules/${module.code}`}
                          className="rounded-sm font-medium text-text hover:text-primary hover:underline"
                        >
                          <span className="font-mono text-sm">{module.code}</span> {module.title}
                        </Link>
                        <p className="text-xs text-muted">
                          {module.credits} credits · {formatSemester(module.semester)}
                        </p>
                      </div>
                      <span
                        className={cn(
                          'shrink-0 text-sm tabular-nums',
                          unpublished ? 'text-muted' : 'font-medium text-text',
                        )}
                      >
                        {result}
                      </span>
                    </li>
                  )
                })}
              </ul>
            </section>
          ))}
        </div>
      )}
    </Card>
  )
}
