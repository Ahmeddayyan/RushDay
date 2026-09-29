import {
  FlaskConical,
  GraduationCap,
  Presentation,
  ShieldCheck,
  type LucideIcon,
} from 'lucide-react'

import type { DemoAccountInfo } from '@/api/types/public'
import { Button } from '@/components/ui'
import { formatRole } from '@/lib/format'

const roleIcon: Record<DemoAccountInfo['role'], LucideIcon> = {
  Student: GraduationCap,
  Lecturer: Presentation,
  Admin: ShieldCheck,
}

export interface DemoAccountsProps {
  accounts: DemoAccountInfo[]
  /** Fills the sign-in form with this account and moves focus to Sign in. */
  onUse: (account: DemoAccountInfo) => void
}

/**
 * The demo accounts panel (05-frontend.md section 10, `/login`), shown only while
 * `status.demo` is not null: one row per role with the published password and what the account
 * shows off, plus a "Use" button.
 */
export function DemoAccounts({ accounts, onUse }: DemoAccountsProps) {
  return (
    <section
      aria-labelledby="demo-accounts-heading"
      className="rounded-lg border border-border bg-surface p-4 shadow-card md:p-6"
    >
      <div className="flex items-start gap-3">
        <span
          aria-hidden="true"
          className="flex size-9 shrink-0 items-center justify-center rounded-full bg-info-soft text-info"
        >
          <FlaskConical className="size-4.5" />
        </span>
        <div className="min-w-0">
          <h2 id="demo-accounts-heading" className="text-base font-semibold text-text">
            Try the demo
          </h2>
          <p className="text-sm text-muted">
            Demo data: 20,000 synthetic students, no real people.
          </p>
        </div>
      </div>
      <ul className="mt-4 divide-y divide-border">
        {accounts.map((account) => {
          const Icon = roleIcon[account.role]
          const role = formatRole(account.role)
          return (
            <li
              key={account.username}
              className="flex flex-col gap-2 py-3 first:pt-0 last:pb-0 sm:flex-row sm:items-start sm:gap-4"
            >
              <div className="min-w-0 flex-1 space-y-1">
                <p className="flex items-center gap-2 text-sm font-semibold text-text">
                  <Icon aria-hidden="true" className="size-4 text-muted" />
                  {role}
                </p>
                <p className="flex flex-wrap items-center gap-x-3 gap-y-1 text-sm text-muted">
                  <span>
                    <span className="sr-only">Username </span>
                    <code className="rounded bg-surface-2 px-1.5 py-0.5 text-text">
                      {account.username}
                    </code>
                  </span>
                  <span>
                    <span className="sr-only">Password </span>
                    <code className="rounded bg-surface-2 px-1.5 py-0.5 text-text">
                      {account.password}
                    </code>
                  </span>
                </p>
                <p className="text-sm text-muted">{account.hint}</p>
              </div>
              <Button
                variant="secondary"
                size="sm"
                className="self-start"
                aria-label={`Use the ${role} demo account`}
                onClick={() => onUse(account)}
              >
                Use
              </Button>
            </li>
          )
        })}
      </ul>
    </section>
  )
}
