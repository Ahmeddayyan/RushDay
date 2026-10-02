import { useState, type ReactNode } from 'react'
import { useQuery } from '@tanstack/react-query'
import {
  Download,
  KeyRound,
  LogOut,
  Palette,
  ShieldCheck,
  ShieldOff,
  UserRound,
} from 'lucide-react'
import { useNavigate } from 'react-router'

import { apiFetch } from '@/api/client'
import { queryKeys, queryTimings } from '@/api/keys'
import { describeProblem } from '@/api/problem'
import type { Me } from '@/api/types/common'
import { useAuth } from '@/app/AuthProvider'
import { ThemeToggle } from '@/components/layout/ThemeToggle'
import {
  Badge,
  Button,
  ButtonLink,
  Card,
  CardDescription,
  CardHeader,
  CardTitle,
  PageHeader,
  Skeleton,
} from '@/components/ui'
import { downloadFromApi } from '@/lib/download'
import { formatRole } from '@/lib/format'
import { toast } from '@/lib/toast'
import { useDocumentTitle } from '@/lib/useDocumentTitle'

/** The two fields of `GET /api/me/dashboard` the profile card shows (the full shape is stage S7's). */
interface StudentProfile {
  programme: string
  yearOfStudy: number
}

function Row({ label, children }: { label: string; children: ReactNode }) {
  return (
    <div className="grid gap-1 py-3 first:pt-0 last:pb-0 sm:grid-cols-[12rem_minmax(0,1fr)] sm:gap-4">
      <dt className="text-sm font-medium text-muted">{label}</dt>
      <dd className="min-w-0 text-sm text-text">{children}</dd>
    </div>
  )
}

function SettingRow({
  title,
  description,
  action,
}: {
  title: ReactNode
  description: ReactNode
  action?: ReactNode
}) {
  return (
    <div className="flex flex-col gap-3 py-4 first:pt-0 last:pb-0 sm:flex-row sm:items-center sm:justify-between sm:gap-6">
      <div className="min-w-0 space-y-1">
        <h3 className="flex flex-wrap items-center gap-2 text-sm font-semibold text-text">
          {title}
        </h3>
        <div className="text-sm text-muted">{description}</div>
      </div>
      {action && <div className="shrink-0">{action}</div>}
    </div>
  )
}

function ProfileCard({ user }: { user: Me }) {
  const isStudent = user.role === 'Student'
  const profile = useQuery({
    queryKey: queryKeys.student.dashboard,
    queryFn: () => apiFetch<StudentProfile>('/api/me/dashboard'),
    enabled: isStudent,
    ...queryTimings.studentDashboard,
  })

  return (
    <Card>
      <CardHeader>
        <CardTitle className="flex items-center gap-2">
          <UserRound aria-hidden="true" className="size-5 text-muted" />
          Profile
        </CardTitle>
      </CardHeader>
      <dl className="divide-y divide-border">
        <Row label="Name">{user.displayName}</Row>
        <Row label="Username">
          <span className="font-mono">{user.username}</span>
        </Row>
        <Row label="Role">
          <Badge variant="info">{formatRole(user.role)}</Badge>
        </Row>
        {user.studentNumber && (
          <Row label="Student number">
            <span className="font-mono">{user.studentNumber}</span>
          </Row>
        )}
        {user.staffNumber && (
          <Row label="Staff number">
            <span className="font-mono">{user.staffNumber}</span>
          </Row>
        )}
        {isStudent && profile.isPending && (
          <Row label="Programme">
            <Skeleton className="h-4 w-48" />
          </Row>
        )}
        {isStudent && profile.data && (
          <>
            <Row label="Programme">{profile.data.programme}</Row>
            <Row label="Year of study">Year {profile.data.yearOfStudy}</Row>
          </>
        )}
      </dl>
    </Card>
  )
}

function SecurityCard({ user }: { user: Me }) {
  const showMfa = user.role !== 'Student'
  const mfaDescription = user.isDemo
    ? "Demo accounts can't enable two-step verification."
    : user.role === 'Admin'
      ? 'Required for administrators. Sign-in asks for a code from an app on your phone.'
      : 'Optional. Sign-in asks for a code from an app on your phone as well as your password.'

  return (
    <Card>
      <CardHeader>
        <CardTitle className="flex items-center gap-2">
          <KeyRound aria-hidden="true" className="size-5 text-muted" />
          Sign-in and security
        </CardTitle>
      </CardHeader>
      <div className="divide-y divide-border">
        <SettingRow
          title="Password"
          description={
            user.isDemo
              ? "Demo accounts can't change their password."
              : 'At least 12 characters. Changing it signs out your other devices.'
          }
          action={
            user.isDemo ? undefined : (
              <ButtonLink to="/account/password" variant="secondary" size="sm">
                Change password
              </ButtonLink>
            )
          }
        />
        {showMfa && (
          <SettingRow
            title={
              <>
                Two-step verification
                {user.mfaEnabled ? (
                  <Badge variant="success" icon={ShieldCheck}>
                    On
                  </Badge>
                ) : (
                  <Badge variant={user.role === 'Admin' ? 'warning' : 'neutral'} icon={ShieldOff}>
                    Off
                  </Badge>
                )}
              </>
            }
            description={mfaDescription}
            action={
              !user.mfaEnabled && !user.isDemo ? (
                <ButtonLink
                  to="/account/mfa"
                  variant={user.role === 'Admin' ? 'primary' : 'secondary'}
                  size="sm"
                >
                  Set up
                </ButtonLink>
              ) : undefined
            }
          />
        )}
      </div>
    </Card>
  )
}

function DataCard({ studentNumber }: { studentNumber: string }) {
  const [pending, setPending] = useState(false)

  async function downloadData() {
    setPending(true)
    try {
      await downloadFromApi('/api/me/export.json', `rushday-${studentNumber}.json`)
      toast.success('Your data has been downloaded.')
    } catch (error) {
      toast.error(describeProblem(error).message)
    } finally {
      setPending(false)
    }
  }

  return (
    <Card>
      <CardHeader>
        <CardTitle className="flex items-center gap-2">
          <Download aria-hidden="true" className="size-5 text-muted" />
          Your data
        </CardTitle>
        <CardDescription>
          Your student record, enrolments and published marks as a JSON file, as held by RushDay.
        </CardDescription>
      </CardHeader>
      <Button
        variant="secondary"
        loading={pending}
        onClick={() => void downloadData()}
        className="h-auto min-h-10 max-w-full py-2 text-left whitespace-normal"
      >
        Download my data (JSON), downloads a file
      </Button>
    </Card>
  )
}

/**
 * `/account` (05-frontend.md section 10): profile, password and two-step verification, signing out
 * everywhere, a student's data export, and appearance.
 */
export function Component() {
  useDocumentTitle('Account · RushDay')
  const { user, logout } = useAuth()
  const navigate = useNavigate()
  const [signingOut, setSigningOut] = useState(false)

  if (!user) return null

  async function signOut() {
    setSigningOut(true)
    try {
      await logout()
      void navigate('/login', { replace: true })
    } catch (error) {
      toast.error(`Couldn't sign you out. ${describeProblem(error).message}`)
      setSigningOut(false)
    }
  }

  return (
    <div className="max-w-3xl">
      <PageHeader
        title="Account"
        description="Your profile, how you sign in, and how RushDay looks."
      />
      <div className="flex flex-col gap-6">
        <ProfileCard user={user} />
        <SecurityCard user={user} />
        <Card>
          <CardHeader>
            <CardTitle className="flex items-center gap-2">
              <LogOut aria-hidden="true" className="size-5 text-muted" />
              Sign out everywhere
            </CardTitle>
            <CardDescription>Signing out ends your sessions on every device.</CardDescription>
          </CardHeader>
          <Button variant="secondary" loading={signingOut} onClick={() => void signOut()}>
            <LogOut aria-hidden="true" className="size-4" />
            Sign out
          </Button>
        </Card>
        {user.role === 'Student' && user.studentNumber && (
          <DataCard studentNumber={user.studentNumber} />
        )}
        <Card>
          <CardHeader>
            <CardTitle id="appearance-heading" className="flex items-center gap-2">
              <Palette aria-hidden="true" className="size-5 text-muted" />
              Appearance
            </CardTitle>
            <CardDescription>
              System follows your device&apos;s light or dark setting.
            </CardDescription>
          </CardHeader>
          <ThemeToggle labelledBy="appearance-heading" />
        </Card>
      </div>
    </div>
  )
}
