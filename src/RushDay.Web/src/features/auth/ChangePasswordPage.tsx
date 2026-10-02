import { useState, type ReactNode } from 'react'
import { zodResolver } from '@hookform/resolvers/zod'
import { Check, Circle, KeyRound, Lock } from 'lucide-react'
import { useForm, useWatch } from 'react-hook-form'
import { useNavigate, useSearchParams } from 'react-router'

import * as authApi from '@/api/endpoints/auth'
import { isProblem, mapFieldErrors, describeProblem, weakPasswordReasons } from '@/api/problem'
import { useAuth } from '@/app/AuthProvider'
import { roleHome } from '@/components/layout/navItems'
import {
  Button,
  ButtonLink,
  Card,
  EmptyState,
  FormError,
  FormField,
  PageHeader,
  PasswordInput,
} from '@/components/ui'
import { cn } from '@/lib/cn'
import { sanitizeReturnTo } from '@/lib/returnTo'
import { toast } from '@/lib/toast'
import { useDirtyForm } from '@/lib/useDirtyForm'
import { useDocumentTitle } from '@/lib/useDocumentTitle'

import { changePasswordSchema, passwordChecks, type ChangePasswordFormValues } from './schemas'

function RequiredBanner({ children }: { children: ReactNode }) {
  return (
    <div className="mb-6 flex items-start gap-3 rounded-lg border border-warning/30 bg-warning-soft px-4 py-3 text-sm text-warning">
      <Lock aria-hidden="true" className="mt-0.5 size-4 shrink-0" />
      <p className="font-medium">{children}</p>
    </div>
  )
}

function PasswordForm({
  username,
  onChanged,
}: {
  username: string
  onChanged: () => Promise<void>
}) {
  const [formError, setFormError] = useState<string | null>(null)
  const {
    register,
    handleSubmit,
    setError,
    control,
    formState: { errors, isSubmitting, isDirty },
  } = useForm<ChangePasswordFormValues>({
    resolver: zodResolver(changePasswordSchema),
    mode: 'onBlur',
    reValidateMode: 'onChange',
    defaultValues: { currentPassword: '', newPassword: '', confirmPassword: '' },
  })

  // Typed-in passwords are unsaved work: if the session ends meanwhile, ReauthDialog keeps them.
  useDirtyForm(isDirty)

  const newPassword = useWatch({ control, name: 'newPassword' })
  const currentPassword = useWatch({ control, name: 'currentPassword' })
  const checks = passwordChecks(newPassword, currentPassword, username)

  const onSubmit = handleSubmit(async ({ currentPassword: current, newPassword: next }) => {
    setFormError(null)
    try {
      await authApi.changePassword({ currentPassword: current, newPassword: next })
      await onChanged()
    } catch (error) {
      if (isProblem(error, 'invalid-current-password')) {
        setError(
          'currentPassword',
          { message: 'Your current password is incorrect.' },
          { shouldFocus: true },
        )
      } else if (isProblem(error, 'weak-password')) {
        setError(
          'newPassword',
          { message: weakPasswordReasons(error).join(' ') },
          { shouldFocus: true },
        )
      } else if (isProblem(error, 'validation')) {
        const { fields, other } = mapFieldErrors(error, ['currentPassword', 'newPassword'] as const)
        if (fields.currentPassword) setError('currentPassword', { message: fields.currentPassword })
        if (fields.newPassword) setError('newPassword', { message: fields.newPassword })
        setFormError(other.join(' ') || null)
      } else {
        setFormError(describeProblem(error).message)
      }
    }
  })

  return (
    <form onSubmit={(event) => void onSubmit(event)} noValidate className="flex flex-col gap-5">
      <FormField label="Current password" error={errors.currentPassword?.message} required>
        <PasswordInput {...register('currentPassword')} autoComplete="current-password" />
      </FormField>

      <div className="flex flex-col gap-3">
        <FormField
          label="New password"
          error={errors.newPassword?.message}
          required
          hint="At least 12 characters. A few unrelated words make a strong password that is easy to remember."
        >
          <PasswordInput {...register('newPassword')} autoComplete="new-password" />
        </FormField>
        <div className="rounded-md border border-border bg-surface-2 px-3.5 py-3">
          <p
            id="password-checklist-heading"
            className="mb-2 text-xs font-semibold tracking-wide text-muted uppercase"
          >
            Your new password
          </p>
          <ul
            aria-labelledby="password-checklist-heading"
            aria-live="polite"
            className="grid gap-1.5 sm:grid-cols-2"
          >
            {checks.map((check) => (
              <li
                key={check.id}
                className={cn(
                  'flex items-center gap-2 text-sm',
                  check.met ? 'text-success' : 'text-muted',
                )}
              >
                {check.met ? (
                  <Check aria-hidden="true" className="size-4 shrink-0" />
                ) : (
                  <Circle aria-hidden="true" className="size-4 shrink-0" />
                )}
                <span>{check.label}</span>
                <span className="sr-only">{check.met ? ': done' : ': not yet'}</span>
              </li>
            ))}
          </ul>
        </div>
      </div>

      <FormField label="Confirm new password" error={errors.confirmPassword?.message} required>
        <PasswordInput {...register('confirmPassword')} autoComplete="new-password" />
      </FormField>

      <FormError>{formError}</FormError>

      <div>
        <Button type="submit" loading={isSubmitting}>
          Change password
        </Button>
      </div>
    </form>
  )
}

/**
 * `/account/password` (05-frontend.md section 10): current, new and confirm, with a live checklist.
 * With `?required=1` (an administrator set a temporary password) the shell hides navigation and a
 * banner explains why; success continues to the next gate or the stored returnTo.
 */
export function Component() {
  useDocumentTitle('Change password · RushDay')
  const { user, refreshUser } = useAuth()
  const [params] = useSearchParams()
  const navigate = useNavigate()

  if (!user) return null
  const required = user.mustChangePassword
  const returnTo = sanitizeReturnTo(params.get('returnTo'))

  async function onChanged() {
    toast.success('Your password has been changed.')
    const me = await refreshUser()
    if (!me) return
    void navigate(returnTo ?? (required ? roleHome(me.role) : '/account'), { replace: true })
  }

  return (
    <div className="max-w-2xl">
      <PageHeader
        title="Change password"
        description={required ? undefined : 'Choose a new password for signing in to RushDay.'}
      />
      {required && (
        <RequiredBanner>
          Your administrator set a temporary password. Choose a new one to continue.
        </RequiredBanner>
      )}
      <Card>
        {user.isDemo ? (
          <EmptyState
            icon={KeyRound}
            title="Demo accounts can't change their password."
            description="The demo password is published on the sign-in page so every visitor can use it."
            action={
              <ButtonLink to="/account" variant="secondary">
                Back to your account
              </ButtonLink>
            }
            compact
          />
        ) : (
          <PasswordForm username={user.username} onChanged={onChanged} />
        )}
      </Card>
    </div>
  )
}
