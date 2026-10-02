import { useEffect, useRef, useState, type FormEvent } from 'react'
import * as RadixDialog from '@radix-ui/react-dialog'
import { KeyRound } from 'lucide-react'

import { ApiError } from '@/api/client'
import * as authApi from '@/api/endpoints/auth'
import { describeProblem } from '@/api/problem'
import type { Me } from '@/api/types/common'
import { Button, FormError, FormField, Input, PasswordInput } from '@/components/ui'
import { overlayClassName, panelClassName } from '@/components/ui/dialog-styles'
import { cn } from '@/lib/cn'

export interface ReauthDialogProps {
  /** Fixed: this is the same person signing in again. */
  username: string
  onSignedIn: (me: Me) => void
  /** Cancel (or Escape) takes the normal path: the session ends and the guard sends them to sign in. */
  onCancel: () => void
  /** Stores the anonymous token fetched for the sign-in form, and the MFA challenge's token. */
  onToken: (token: string) => void
}

/**
 * Sign in again without leaving the page (05-frontend.md section 5.1 step 4): opened by
 * AuthProvider when a request answers 401 while a form has unsaved work. The username is fixed; the
 * person types the password (and a code when the account has two-step verification). Success
 * re-issues the CSRF token through the normal sign-in completion, and the page, with its unsaved
 * changes, is still there.
 */
export function ReauthDialog({ username, onSignedIn, onCancel, onToken }: ReauthDialogProps) {
  const [step, setStep] = useState<'password' | 'code'>('password')
  const [password, setPassword] = useState('')
  const [code, setCode] = useState('')
  const [pending, setPending] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const tokenReady = useRef<Promise<void> | null>(null)
  const passwordRef = useRef<HTMLInputElement>(null)
  const codeRef = useRef<HTMLInputElement>(null)

  // The old token was bound to the session that ended: fetch one for the anonymous sign-in form.
  useEffect(() => {
    tokenReady.current = authApi
      .getCsrf()
      .then(({ csrfToken }) => onToken(csrfToken))
      .catch(() => {})
  }, [onToken])

  useEffect(() => {
    if (step === 'code') codeRef.current?.focus()
  }, [step])

  async function submit(event: FormEvent) {
    event.preventDefault()
    setError(null)
    if (step === 'password' && !password) {
      setError('Enter your password.')
      passwordRef.current?.focus()
      return
    }
    if (step === 'code' && !/^\d{6}$/.test(code)) {
      setError('Enter the 6-digit code from your authenticator app.')
      codeRef.current?.focus()
      return
    }

    setPending(true)
    try {
      await tokenReady.current
      if (step === 'password') {
        const response = await authApi.login({ username, password })
        if (authApi.isMfaChallenge(response)) {
          onToken(response.csrfToken)
          setStep('code')
          return
        }
        onSignedIn(response)
      } else {
        onSignedIn(await authApi.verifyMfa(code))
      }
    } catch (caught) {
      if (caught instanceof ApiError && caught.status === 401) {
        setError(
          step === 'code'
            ? "That code didn't work. Check the time on your phone and try the newest code."
            : 'Incorrect password.',
        )
      } else {
        setError(describeProblem(caught).message)
      }
    } finally {
      setPending(false)
    }
  }

  return (
    <RadixDialog.Root
      open
      onOpenChange={(open) => {
        if (!open) onCancel()
      }}
    >
      <RadixDialog.Portal>
        <RadixDialog.Overlay className={overlayClassName} />
        <RadixDialog.Content
          className={cn(panelClassName, 'sm:max-w-[440px]')}
          onInteractOutside={(event) => event.preventDefault()}
          onOpenAutoFocus={(event) => {
            event.preventDefault()
            passwordRef.current?.focus()
          }}
        >
          <form
            onSubmit={(event) => void submit(event)}
            noValidate
            className="flex flex-1 flex-col gap-5 p-5 sm:p-6"
          >
            <div className="flex items-start gap-3">
              <span
                aria-hidden="true"
                className="flex size-10 shrink-0 items-center justify-center rounded-full bg-primary-soft text-primary"
              >
                <KeyRound className="size-5" />
              </span>
              <div className="min-w-0 space-y-1.5">
                <RadixDialog.Title className="text-lg leading-snug font-semibold tracking-tight">
                  Sign in again to keep your changes
                </RadixDialog.Title>
                <RadixDialog.Description className="text-sm text-muted">
                  Your session ended while this page had unsaved changes. Sign in again and they
                  stay exactly where they are.
                </RadixDialog.Description>
              </div>
            </div>

            <FormField label="Username">
              <Input value={username} readOnly autoComplete="username" className="font-mono" />
            </FormField>

            {step === 'password' ? (
              <FormField label="Password">
                <PasswordInput
                  ref={passwordRef}
                  autoComplete="current-password"
                  value={password}
                  onChange={(event) => setPassword(event.target.value)}
                />
              </FormField>
            ) : (
              <FormField
                label="Verification code"
                hint="Type the 6-digit code for RushDay from your authenticator app."
              >
                <Input
                  ref={codeRef}
                  inputMode="numeric"
                  autoComplete="one-time-code"
                  pattern="[0-9]{6}"
                  maxLength={6}
                  value={code}
                  onChange={(event) => setCode(event.target.value.replace(/\D/g, ''))}
                  className="font-mono tracking-[0.3em]"
                />
              </FormField>
            )}

            <FormError>{error}</FormError>

            <div className="mt-1 flex flex-col-reverse gap-2 sm:flex-row sm:justify-end">
              <Button variant="secondary" onClick={onCancel} disabled={pending}>
                Cancel
              </Button>
              <Button type="submit" loading={pending}>
                {step === 'password' ? 'Sign in' : 'Verify'}
              </Button>
            </div>
          </form>
        </RadixDialog.Content>
      </RadixDialog.Portal>
    </RadixDialog.Root>
  )
}
