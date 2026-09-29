import { useEffect, useRef, useState } from 'react'
import { zodResolver } from '@hookform/resolvers/zod'
import { useForm } from 'react-hook-form'

import { ApiError } from '@/api/client'
import { describeProblem } from '@/api/problem'
import { useAuth } from '@/app/AuthProvider'
import { Button, FormError, FormField, Input, SupportLink } from '@/components/ui'

import { mfaCodeSchema, type MfaCodeFormValues } from './schemas'

const WRONG_CODE = "That code didn't work. Check the time on your phone and try the newest code."

/**
 * The second step of sign-in while `status === 'mfaPending'` (05-frontend.md section 10): a
 * six-digit code from the authenticator app. A wrong code keeps this step; "Start again" returns to
 * the password form.
 */
export function MfaCodeStep() {
  const { verifyMfa, cancelMfa } = useAuth()
  const [formError, setFormError] = useState<string | null>(null)
  const inputRef = useRef<HTMLInputElement | null>(null)
  const {
    register,
    handleSubmit,
    setError,
    formState: { errors, isSubmitting },
  } = useForm<MfaCodeFormValues>({
    resolver: zodResolver(mfaCodeSchema),
    mode: 'onBlur',
    reValidateMode: 'onChange',
    defaultValues: { code: '' },
  })
  const { ref: registerRef, ...codeField } = register('code')

  // The password step just disappeared: put focus where the next thing to type is.
  useEffect(() => {
    inputRef.current?.focus()
  }, [])

  const onSubmit = handleSubmit(async ({ code }) => {
    setFormError(null)
    try {
      await verifyMfa(code)
    } catch (error) {
      if (error instanceof ApiError && error.status === 401) {
        setError('code', { message: WRONG_CODE }, { shouldFocus: true })
      } else {
        setFormError(describeProblem(error).message)
      }
    }
  })

  return (
    <div className="flex flex-col gap-5">
      <div className="space-y-1.5">
        <h1
          tabIndex={-1}
          className="text-xl font-semibold tracking-tight text-text outline-none md:text-2xl"
        >
          Enter your verification code
        </h1>
        <p className="text-sm text-muted">
          Open your authenticator app and type the 6-digit code for RushDay.
        </p>
      </div>

      <form onSubmit={(event) => void onSubmit(event)} noValidate className="flex flex-col gap-4">
        <FormField label="Verification code" error={errors.code?.message}>
          <Input
            {...codeField}
            ref={(node) => {
              registerRef(node)
              inputRef.current = node
            }}
            inputMode="numeric"
            autoComplete="one-time-code"
            pattern="[0-9]{6}"
            maxLength={6}
            placeholder="123456"
            className="font-mono text-lg tracking-[0.3em]"
          />
        </FormField>
        <FormError>{formError}</FormError>
        <div className="flex flex-col gap-2 sm:flex-row-reverse sm:justify-start">
          <Button type="submit" loading={isSubmitting} className="sm:min-w-32">
            Verify
          </Button>
          <Button variant="ghost" onClick={cancelMfa} disabled={isSubmitting}>
            Start again
          </Button>
        </div>
      </form>

      <p className="text-sm text-muted">
        Lost your phone? Ask another administrator to reset your two-step verification, or{' '}
        <SupportLink />.
      </p>
    </div>
  )
}
