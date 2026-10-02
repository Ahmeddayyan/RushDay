import { useEffect, useRef, useState, type ReactNode } from 'react'
import { zodResolver } from '@hookform/resolvers/zod'
import { useMutation } from '@tanstack/react-query'
import { Check, Copy, ShieldAlert, ShieldCheck, Smartphone } from 'lucide-react'
import { useForm } from 'react-hook-form'
import { useNavigate, useSearchParams } from 'react-router'

import * as authApi from '@/api/endpoints/auth'
import { describeProblem, isProblem } from '@/api/problem'
import type { Me } from '@/api/types/common'
import { useAuth } from '@/app/AuthProvider'
import { roleHome } from '@/components/layout/navItems'
import {
  Button,
  ButtonLink,
  Card,
  EmptyState,
  ErrorState,
  FormError,
  FormField,
  Input,
  LoadingRegion,
  PageHeader,
  Skeleton,
} from '@/components/ui'
import { qrDataUrl } from '@/lib/qr'
import { sanitizeReturnTo } from '@/lib/returnTo'
import { toast } from '@/lib/toast'
import { useDocumentTitle } from '@/lib/useDocumentTitle'

import { mfaCodeSchema, type MfaCodeFormValues } from './schemas'

const WRONG_CODE = "That code didn't work. Check the time on your phone and try the newest code."

function StepHeading({ step, children }: { step: number; children: ReactNode }) {
  return (
    <h2 className="flex items-start gap-3 text-base font-semibold text-text">
      <span
        aria-hidden="true"
        className="flex size-7 shrink-0 items-center justify-center rounded-full bg-primary-soft text-sm font-semibold text-primary"
      >
        {step}
      </span>
      <span className="pt-0.5">
        <span className="sr-only">Step {step}: </span>
        {children}
      </span>
    </h2>
  )
}

function CopyKey({ sharedKey }: { sharedKey: string }) {
  const [copied, setCopied] = useState(false)

  async function copy() {
    try {
      await navigator.clipboard.writeText(sharedKey.replace(/\s+/g, ''))
      setCopied(true)
      setTimeout(() => setCopied(false), 2500)
    } catch {
      toast.error('Copying is blocked in this browser. Select the key and copy it instead.')
    }
  }

  return (
    <Button variant="secondary" size="sm" onClick={() => void copy()} aria-label="Copy the key">
      {copied ? (
        <Check aria-hidden="true" className="size-4" />
      ) : (
        <Copy aria-hidden="true" className="size-4" />
      )}
      <span aria-live="polite">{copied ? 'Copied' : 'Copy'}</span>
    </Button>
  )
}

function SetupFlow({ onEnabled }: { onEnabled: (me: Me) => void }) {
  const setup = useMutation({ mutationFn: authApi.setupMfa })
  const { mutate: startSetup } = setup
  const started = useRef(false)
  const [qr, setQr] = useState<{ uri: string; image: string } | null>(null)
  const [formError, setFormError] = useState<string | null>(null)

  // Once per visit: every call replaces the key, so React StrictMode's second effect run (which
  // would silently invalidate the QR code on screen) is skipped by the ref.
  useEffect(() => {
    if (started.current) return
    started.current = true
    startSetup()
  }, [startSetup])

  const otpauthUri = setup.data?.otpauthUri
  useEffect(() => {
    if (!otpauthUri) return
    let live = true
    qrDataUrl(otpauthUri)
      .then((image) => {
        if (live) setQr({ uri: otpauthUri, image })
      })
      .catch(() => {})
    return () => {
      live = false
    }
  }, [otpauthUri])

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

  const onSubmit = handleSubmit(async ({ code }) => {
    setFormError(null)
    try {
      onEnabled(await authApi.enableMfa(code))
    } catch (error) {
      if (isProblem(error, 'invalid-mfa-code')) {
        setError('code', { message: WRONG_CODE }, { shouldFocus: true })
      } else if (isProblem(error, 'demo-account')) {
        setFormError("Demo accounts can't enable two-step verification.")
      } else {
        setFormError(describeProblem(error).message)
      }
    }
  })

  if (setup.isError) {
    if (isProblem(setup.error, 'demo-account')) {
      return (
        <EmptyState
          icon={ShieldAlert}
          title="Demo accounts can't enable two-step verification."
          compact
        />
      )
    }
    return <ErrorState error={setup.error} onRetry={() => startSetup()} compact />
  }

  const data = setup.data
  const image = qr && data && qr.uri === data.otpauthUri ? qr.image : null

  return (
    <div className="flex flex-col gap-8">
      <section className="flex flex-col gap-4">
        <StepHeading step={1}>
          Scan this code with an authenticator app (Microsoft Authenticator, Google Authenticator or
          any TOTP app)
        </StepHeading>
        {!data ? (
          <LoadingRegion label="your setup code">
            <div className="flex flex-col gap-4 sm:flex-row sm:items-start">
              <Skeleton className="size-48 rounded-lg" />
              <div className="flex-1 space-y-2">
                <Skeleton className="h-4 w-40" />
                <Skeleton className="h-9 w-full max-w-sm" />
              </div>
            </div>
          </LoadingRegion>
        ) : (
          <div className="flex flex-col gap-5 sm:flex-row sm:items-start">
            <div className="flex size-52 shrink-0 items-center justify-center rounded-lg border border-border bg-white p-2">
              {image ? (
                <img
                  src={image}
                  alt="QR code for your authenticator app"
                  width={192}
                  height={192}
                  className="size-48"
                />
              ) : (
                <Skeleton className="size-48" />
              )}
            </div>
            <div className="min-w-0 flex-1 space-y-2">
              <p className="text-sm text-muted">Can&apos;t scan? Type this key instead.</p>
              <div className="flex flex-wrap items-center gap-2">
                <code
                  aria-label="Setup key"
                  className="rounded-md border border-border bg-surface-2 px-3 py-2 text-sm break-all text-text"
                >
                  {data.sharedKey}
                </code>
                <CopyKey sharedKey={data.sharedKey} />
              </div>
              <p className="text-sm text-muted">
                The key is time-based (TOTP), six digits, every 30 seconds.
              </p>
            </div>
          </div>
        )}
      </section>

      <section className="flex flex-col gap-4">
        <StepHeading step={2}>Enter the 6-digit code it shows</StepHeading>
        <form
          onSubmit={(event) => void onSubmit(event)}
          noValidate
          className="flex flex-col gap-4 sm:max-w-sm"
        >
          <FormField label="Verification code" error={errors.code?.message}>
            <Input
              {...register('code')}
              inputMode="numeric"
              autoComplete="one-time-code"
              pattern="[0-9]{6}"
              maxLength={6}
              placeholder="123456"
              disabled={!data}
              className="font-mono text-lg tracking-[0.3em]"
            />
          </FormField>
          <FormError>{formError}</FormError>
          <div>
            <Button type="submit" loading={isSubmitting} disabled={!data}>
              Turn on two-step verification
            </Button>
          </div>
        </form>
      </section>
    </div>
  )
}

/**
 * `/account/mfa` (05-frontend.md section 10): a fresh authenticator key as a QR code (drawn in the
 * browser by the lazily loaded `qrcode` package) and as text, then the first code to prove the app
 * holds it. With `?required=1` (administrators without a second factor) navigation is hidden.
 */
export function Component() {
  useDocumentTitle('Two-step verification · RushDay')
  const { user, completeSession } = useAuth()
  const [params] = useSearchParams()
  const navigate = useNavigate()

  if (!user) return null
  const required = user.mfaSetupRequired
  const returnTo = sanitizeReturnTo(params.get('returnTo'))

  function onEnabled(me: Me) {
    completeSession(me)
    toast.success('Two-step verification is on.')
    void navigate(returnTo ?? (required ? roleHome(me.role) : '/account'), { replace: true })
  }

  let body: ReactNode
  if (user.mfaEnabled) {
    body = (
      <EmptyState
        icon={ShieldCheck}
        title="Two-step verification is on for this account."
        description="You'll be asked for a code from your authenticator app each time you sign in."
        action={
          <ButtonLink to="/account" variant="secondary">
            Back to your account
          </ButtonLink>
        }
        compact
      />
    )
  } else if (user.isDemo) {
    body = (
      <EmptyState
        icon={ShieldAlert}
        title="Demo accounts can't enable two-step verification."
        description="The demo stays open to every visitor, so it never asks for a code."
        compact
      />
    )
  } else {
    body = <SetupFlow onEnabled={onEnabled} />
  }

  return (
    <div className="max-w-3xl">
      <PageHeader
        title="Two-step verification"
        description="Sign-in asks for your password and a 6-digit code from an app on your phone, so a stolen password alone is not enough."
      />
      {required && (
        <div className="mb-6 flex items-start gap-3 rounded-lg border border-warning/30 bg-warning-soft px-4 py-3 text-sm text-warning">
          <Smartphone aria-hidden="true" className="mt-0.5 size-4 shrink-0" />
          <p className="font-medium">
            Administrators must use two-step verification. Set it up to continue.
          </p>
        </div>
      )}
      <Card>{body}</Card>
    </div>
  )
}
