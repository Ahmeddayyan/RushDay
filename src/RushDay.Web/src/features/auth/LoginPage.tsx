import { useState, type FormEvent } from 'react'
import { Navigate, useSearchParams } from 'react-router'

import { useAuth } from '@/app/AuthProvider'
import {
  Button,
  Card,
  CardContent,
  CardDescription,
  CardHeader,
  CardTitle,
  Input,
} from '@/components/ui'
import { useDocumentTitle } from '@/lib/useDocumentTitle'

/**
 * Placeholder for stage S3: enough to route to, sign in against the real API and exercise the guards.
 * The full page (MfaCodeStep, DemoAccounts, the story strapline, lockout copy, ...) is stage S5's job.
 */
export function Component() {
  useDocumentTitle('Sign in · RushDay')
  const { status, login } = useAuth()
  const [searchParams] = useSearchParams()
  const [username, setUsername] = useState('')
  const [password, setPassword] = useState('')
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)

  if (status === 'authenticated') {
    const returnTo = searchParams.get('returnTo')
    return <Navigate to={returnTo && returnTo.startsWith('/') ? returnTo : '/'} replace />
  }

  async function submit(event: FormEvent) {
    event.preventDefault()
    setBusy(true)
    setError(null)
    try {
      await login(username, password)
    } catch {
      setError('Incorrect username or password.')
    } finally {
      setBusy(false)
    }
  }

  return (
    <div className="flex min-h-dvh items-center justify-center bg-background px-4 py-8">
      <Card className="w-full max-w-sm">
        <CardHeader>
          <CardTitle as="h1" className="text-xl">
            Sign in
          </CardTitle>
          <CardDescription>Student number, staff number or admin username.</CardDescription>
        </CardHeader>
        <CardContent>
          <form onSubmit={(event) => void submit(event)} noValidate className="flex flex-col gap-4">
            <Input
              label="Student number, staff number or admin username"
              autoComplete="username"
              value={username}
              onChange={(event) => setUsername(event.target.value)}
            />
            <Input
              label="Password"
              type="password"
              autoComplete="current-password"
              value={password}
              onChange={(event) => setPassword(event.target.value)}
            />
            {error && (
              <p role="alert" className="text-sm text-danger">
                {error}
              </p>
            )}
            <Button type="submit" loading={busy} className="w-full">
              Sign in
            </Button>
          </form>
        </CardContent>
      </Card>
    </div>
  )
}
