import { useEffect, useRef, useState, type ReactNode, type RefObject } from 'react'
import { zodResolver } from '@hookform/resolvers/zod'
import { useQueryClient } from '@tanstack/react-query'
import { ArrowRight, CalendarClock, CircleCheck, Quote } from 'lucide-react'
import { useForm, type UseFormReturn } from 'react-hook-form'
import { Link } from 'react-router'

import { ApiError } from '@/api/client'
import { usePublicStatus, useServerClock } from '@/api/endpoints/public'
import { queryKeys } from '@/api/keys'
import { describeProblem } from '@/api/problem'
import type { DemoAccountInfo, PublicStatus } from '@/api/types/public'
import { useAuth } from '@/app/AuthProvider'
import { Footer } from '@/components/layout/Footer'
import { SkipLink } from '@/components/layout/SkipLink'
import { ThemeToggle } from '@/components/layout/ThemeToggle'
import { Wordmark } from '@/components/layout/Wordmark'
import {
  Button,
  ColdStartNotice,
  Countdown,
  FormError,
  FormField,
  Input,
  PasswordInput,
  Skeleton,
  SupportLink,
} from '@/components/ui'
import { cn } from '@/lib/cn'
import { formatDate, formatSemester, formatTime, zoneLabel } from '@/lib/format'
import { useDocumentTitle } from '@/lib/useDocumentTitle'

import { DemoAccounts } from './DemoAccounts'
import { MfaCodeStep } from './MfaCodeStep'
import { loginSchema, type LoginFormValues } from './schemas'

/**
 * The owner's story sentence pair (00-overview.md section 1), verbatim. A string literal in this file
 * on purpose: scripts/check-story.ps1 greps the source for it.
 */
const STORY =
  "I built RushDay because my own university's portal fell over every results day and enrolment window. I wanted to understand why that happens and to build a portal that doesn't crash under the same load."

const COLD_START_AFTER_MS = 3_000

/** True once `pending` has lasted `delayMs`: the cold-start notice waits 3 s before it appears. */
function usePendingFor(pending: boolean, delayMs: number): boolean {
  const [slow, setSlow] = useState(false)
  useEffect(() => {
    if (!pending) return
    const timer = setTimeout(() => setSlow(true), delayMs)
    return () => {
      clearTimeout(timer)
      setSlow(false)
    }
  }, [pending, delayMs])
  return pending && slow
}

function StoryQuote({ size }: { size: 'hero' | 'compact' }) {
  return (
    <figure className="space-y-3">
      <blockquote
        className={cn(
          'relative text-text',
          size === 'hero'
            ? 'text-lg leading-relaxed font-medium md:text-xl lg:text-[1.625rem] lg:leading-snug'
            : 'text-base leading-relaxed',
        )}
      >
        <Quote
          aria-hidden="true"
          className={cn('mb-2 text-primary', size === 'hero' ? 'size-6 lg:size-8' : 'size-5')}
        />
        <p>{STORY}</p>
      </blockquote>
      <figcaption className="text-sm text-muted">
        <cite className="not-italic">Ahmed Ayyan, creator of RushDay</cite>
      </figcaption>
    </figure>
  )
}

function StoryLink() {
  return (
    <Link
      to="/story"
      className="inline-flex items-center gap-1.5 rounded-sm text-sm font-medium text-primary underline-offset-4 hover:underline"
    >
      How it holds up under load
      <ArrowRight aria-hidden="true" className="size-4" />
    </Link>
  )
}

/** "{Semester} {academicYear} results publish {date} at {time} ({zone})" with a countdown, or "published". */
function ResultsLine({ status }: { status: PublicStatus }) {
  const queryClient = useQueryClient()
  const { offsetMs } = useServerClock()
  const zone = status.institution.timeZone
  const next = status.nextPublication
  const latest = status.latestPublication?.state === 'live' ? status.latestPublication : null
  const publication = next ?? latest
  if (!publication) return null

  const when = `${formatDate(publication.publishAt, zone)} at ${formatTime(publication.publishAt, zone)} (${zoneLabel(publication.publishAt, zone)})`
  const label = `${formatSemester(publication.semester)} ${publication.academicYear} results`

  return (
    <div className="flex items-start gap-3 rounded-lg border border-border bg-surface p-4 shadow-card">
      <span
        aria-hidden="true"
        className={cn(
          'flex size-9 shrink-0 items-center justify-center rounded-full',
          next ? 'bg-warning-soft text-warning' : 'bg-success-soft text-success',
        )}
      >
        {next ? <CalendarClock className="size-4.5" /> : <CircleCheck className="size-4.5" />}
      </span>
      <div className="min-w-0 space-y-3">
        <p className="text-sm font-medium text-text">
          {label} {next ? 'publish' : 'published'} {when}
        </p>
        {next && (
          <Countdown
            target={next.publishAt}
            timeZone={zone}
            serverOffsetMs={offsetMs}
            sentencePrefix={`${label} publish at`}
            elapsedText="Results are being released"
            onElapsed={() =>
              void queryClient.invalidateQueries({ queryKey: queryKeys.publicStatus })
            }
          />
        )}
      </div>
    </div>
  )
}

interface LoginFormProps {
  form: UseFormReturn<LoginFormValues>
  submitRef: RefObject<HTMLButtonElement | null>
}

function LoginForm({ form, submitRef }: LoginFormProps) {
  const { login } = useAuth()
  const [failures, setFailures] = useState(0)
  const [formError, setFormError] = useState<ReactNode>(null)
  const {
    register,
    handleSubmit,
    formState: { errors, isSubmitting },
  } = form
  const slow = usePendingFor(isSubmitting, COLD_START_AFTER_MS)

  const onSubmit = handleSubmit(async ({ username, password }) => {
    setFormError(null)
    try {
      await login(username, password)
      setFailures(0)
    } catch (error) {
      if (error instanceof ApiError && error.status === 401) {
        const count = failures + 1
        setFailures(count)
        setFormError(
          <>
            <p>Incorrect username or password.</p>
            {count >= 3 && (
              <p>
                Still stuck? Repeated failed attempts can pause sign-in for up to 15 minutes. To
                reset your password, <SupportLink />.
              </p>
            )}
          </>,
        )
      } else if (error instanceof ApiError && error.status === 429) {
        setFormError(`Too many attempts. Try again in ${error.retryAfterSeconds ?? 60}s.`)
      } else {
        setFormError(describeProblem(error).message)
      }
    }
  })

  return (
    <form onSubmit={(event) => void onSubmit(event)} noValidate className="flex flex-col gap-4">
      <FormField
        label="Student number, staff number or admin username"
        error={errors.username?.message}
      >
        <Input
          {...register('username')}
          autoComplete="username"
          autoCapitalize="none"
          autoCorrect="off"
          spellCheck={false}
          placeholder="S000001"
        />
      </FormField>
      <FormField label="Password" error={errors.password?.message}>
        <PasswordInput {...register('password')} autoComplete="current-password" />
      </FormField>
      <FormError>{formError}</FormError>
      {slow && <ColdStartNotice />}
      <Button ref={submitRef} type="submit" size="lg" loading={isSubmitting} className="w-full">
        Sign in
      </Button>
    </form>
  )
}

/**
 * `/login` (05-frontend.md section 10). The wordmark, the form and the footer render at once and
 * never wait for `['public','status']`; the demo panel and the results line appear only when
 * status answers, and a failed status call (cold start, 503) leaves a working form. In demo mode
 * the story is the strapline under the wordmark; otherwise the strapline names the institution
 * and the story sits below the form under "About RushDay".
 */
export function Component() {
  useDocumentTitle('Sign in · RushDay')
  const { status: authStatus } = useAuth()
  const statusQuery = usePublicStatus()
  const status = statusQuery.data
  const statusLoading = statusQuery.isPending
  const demo = status?.demo ?? null
  const storyAbove = demo !== null
  const submitRef = useRef<HTMLButtonElement | null>(null)

  const form = useForm<LoginFormValues>({
    resolver: zodResolver(loginSchema),
    mode: 'onBlur',
    reValidateMode: 'onChange',
    defaultValues: { username: '', password: '' },
  })

  function fillDemoAccount(account: DemoAccountInfo) {
    form.setValue('username', account.username, { shouldValidate: true })
    form.setValue('password', account.password, { shouldValidate: true })
    submitRef.current?.focus()
  }

  return (
    <div className="min-h-dvh bg-background text-text lg:grid lg:grid-cols-[minmax(0,5fr)_minmax(0,6fr)]">
      <SkipLink />

      {/* The column (surface and border) runs the full page height; the story panel inside is sticky. */}
      <div className="lg:border-r lg:border-border lg:bg-surface">
        <header className="relative overflow-hidden px-4 pt-6 pb-2 md:px-8 lg:sticky lg:top-0 lg:flex lg:h-dvh lg:flex-col lg:px-12 lg:py-10 xl:px-16">
          <div
            aria-hidden="true"
            className="pointer-events-none absolute -top-40 -left-40 hidden size-[36rem] rounded-full bg-primary/10 blur-3xl lg:block"
          />
          <div className="relative flex items-center justify-between gap-4">
            <Wordmark size="lg" />
            <ThemeToggle variant="compact" />
          </div>

          <div className="relative mx-auto mt-6 w-full max-w-md space-y-4 lg:mx-0 lg:mt-auto lg:max-w-xl lg:space-y-6">
            {storyAbove ? (
              <>
                <StoryQuote size="hero" />
                <StoryLink />
              </>
            ) : (
              <p className="text-lg font-medium text-text lg:text-2xl">
                {statusLoading ? (
                  <Skeleton className="inline-block h-6 w-64 align-middle" />
                ) : status ? (
                  `${status.institution.name} student portal`
                ) : (
                  'Student portal'
                )}
              </p>
            )}
          </div>

          {status && (
            <div className="relative mx-auto mt-6 w-full max-w-md lg:mx-0 lg:mt-10 lg:mb-auto lg:max-w-xl">
              <ResultsLine status={status} />
            </div>
          )}
        </header>
      </div>

      <div className="flex min-h-full flex-col">
        <main
          id="main"
          tabIndex={-1}
          className="flex flex-1 flex-col justify-center px-4 py-8 outline-none md:px-8 lg:py-12"
        >
          <div className="mx-auto w-full max-w-md space-y-6">
            <div className="rounded-xl border border-border bg-surface p-5 shadow-card sm:p-8">
              {authStatus === 'mfaPending' ? (
                <MfaCodeStep />
              ) : (
                <div className="flex flex-col gap-6">
                  <div className="space-y-1.5">
                    <h1
                      tabIndex={-1}
                      className="text-xl font-semibold tracking-tight text-text outline-none md:text-2xl"
                    >
                      Sign in
                    </h1>
                    <p className="text-sm text-muted">
                      {statusLoading ? (
                        <Skeleton className="inline-block h-4 w-48 align-middle" />
                      ) : status ? (
                        status.institution.name
                      ) : (
                        'Use the account your institution gave you.'
                      )}
                    </p>
                  </div>
                  <LoginForm form={form} submitRef={submitRef} />
                </div>
              )}
            </div>

            {demo && authStatus !== 'mfaPending' && (
              <DemoAccounts accounts={demo.accounts} onUse={fillDemoAccount} />
            )}

            {!storyAbove && (
              <section aria-labelledby="about-rushday-heading" className="space-y-3 px-1">
                <h2 id="about-rushday-heading" className="text-base font-semibold text-text">
                  About RushDay
                </h2>
                <StoryQuote size="compact" />
                <StoryLink />
              </section>
            )}
          </div>
        </main>
        <Footer extended />
      </div>
    </div>
  )
}
