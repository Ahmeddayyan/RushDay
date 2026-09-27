import { zodResolver } from '@hookform/resolvers/zod'
import { useMutation } from '@tanstack/react-query'
import { useForm } from 'react-hook-form'
import { Navigate, useLocation } from 'react-router'

import {
  Button,
  Card,
  CardContent,
  CardDescription,
  CardHeader,
  CardTitle,
  Input,
} from '@/components/ui'
import { loginErrorMessage, loginSchema, useAuth, type LoginFormValues } from '@/features/auth'
import { usePageTitle } from '@/lib/usePageTitle'

/** Where to go after signing in: back to the guarded page that sent us here, else the dashboard. */
function useReturnPath(): string {
  const location = useLocation()
  const state = location.state as { from?: unknown } | null
  return typeof state?.from === 'string' && state.from.startsWith('/') ? state.from : '/'
}

export function LoginPage() {
  usePageTitle('Sign in')
  const { status, signIn } = useAuth()
  const returnPath = useReturnPath()

  const form = useForm<LoginFormValues>({
    resolver: zodResolver(loginSchema),
    defaultValues: { identifier: '', password: '' },
  })
  const login = useMutation({ mutationFn: signIn })

  if (status === 'authenticated') return <Navigate to={returnPath} replace />

  const submit = form.handleSubmit((values) => login.mutate(values))
  const { errors, isSubmitting } = form.formState
  const busy = isSubmitting || login.isPending

  return (
    <Card className="w-full max-w-sm">
      <CardHeader>
        <CardTitle as="h2" className="text-xl">
          Sign in to RushDay
        </CardTitle>
        <CardDescription>Use your university student number or staff username.</CardDescription>
      </CardHeader>
      <CardContent>
        <form onSubmit={submit} noValidate className="flex flex-col gap-4">
          <Input
            label="Student number or username"
            autoComplete="username"
            autoCapitalize="none"
            spellCheck={false}
            placeholder="S000001"
            error={errors.identifier?.message}
            {...form.register('identifier')}
          />
          <Input
            label="Password"
            type="password"
            autoComplete="current-password"
            error={errors.password?.message}
            {...form.register('password')}
          />
          {login.isError && (
            <div
              role="alert"
              className="rounded-md border border-danger/40 bg-danger/10 px-3 py-2 text-sm text-danger"
            >
              {loginErrorMessage(login.error)}
            </div>
          )}
          <Button type="submit" loading={busy} className="w-full">
            Sign in
          </Button>
        </form>
      </CardContent>
    </Card>
  )
}
