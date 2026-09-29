import type { Semester } from '@/api/types/common'
import { Meter } from '@/components/ui'
import { formatSemester } from '@/lib/format'

import { creditBudgetText } from '../copy'
import { CREDIT_LIMIT } from '../enrolState'

export interface CreditBudgetProps {
  semester: Semester
  used: number
  limit?: number
  /** "2026/27"; omitted when the year is not known yet. */
  academicYear?: string | null
  className?: string
}

/**
 * A semester's credit budget (05-frontend.md section 10): this year's active credits against the
 * 60-credit limit, as a bar and as the sentence "15 of 60 credits, Autumn 2026/27".
 */
export function CreditBudget({
  semester,
  used,
  limit = CREDIT_LIMIT,
  academicYear,
  className,
}: CreditBudgetProps) {
  const reached = used >= limit
  return (
    <Meter
      label={`${formatSemester(semester)} credits`}
      value={Math.min(used, limit)}
      max={limit}
      valueText={creditBudgetText(used, limit, semester, academicYear)}
      tone={reached ? 'warning' : 'primary'}
      {...(reached ? { statusText: 'Limit reached' } : {})}
      {...(className ? { className } : {})}
    />
  )
}
