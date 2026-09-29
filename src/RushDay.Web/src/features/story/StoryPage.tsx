import { ExternalLink } from 'lucide-react'

import { useApiIndex } from '@/api/endpoints/public'
import { DashboardKneeCharts } from '@/features/ops/charts/DashboardKneeCharts'
import { EnrolmentRushChart } from '@/features/ops/charts/EnrolmentRushChart'
import { Glossary } from '@/features/ops/charts/Glossary'
import { LoginStormTiles } from '@/features/ops/charts/LoginStormTiles'
import { MachineBanner } from '@/features/ops/charts/MachineBanner'
import { ResultsDayTiles } from '@/features/ops/charts/ResultsDayTiles'
import { useLoadResults } from '@/features/ops/charts/useLoadResults'
import {
  ButtonLink,
  buttonStyles,
  Card,
  CardDescription,
  CardHeader,
  CardTitle,
  LoadingRegion,
  PageHeader,
  Skeleton,
} from '@/components/ui'
import { useDocumentTitle } from '@/lib/useDocumentTitle'

/**
 * `/story` (05-frontend.md section 10): public, no sign-in required. The owner's story, the
 * measured before/after narrative, and the same load-test charts as `/admin/ops`'s "load story"
 * section, rendered from the committed `public/data/load-results.json`. `scripts/check-story.ps1`
 * (stage S11) greps the sentence pair below verbatim, so it must stay a string literal.
 */
export function Component() {
  useDocumentTitle('How RushDay holds up under load · RushDay')
  const results = useLoadResults()
  const index = useApiIndex()
  const githubUrl = index.data?.links.github ?? 'https://github.com/Ahmeddayyan/RushDay'

  return (
    // One centred column: the chart cards use its full width (1152 px) while the prose keeps a
    // readable measure (max-w-3xl, about 75 characters).
    <div className="mx-auto w-full max-w-6xl space-y-8">
      <div className="max-w-3xl space-y-6">
        <PageHeader
          title="How RushDay holds up under load"
          description="The measured story of a portal that used to fall over on results day, and what changed."
        />

        <blockquote className="space-y-2 border-l-4 border-primary pl-4 text-lg text-text">
          <p>
            I built RushDay because my own university&apos;s portal fell over every results day and
            enrolment window. I wanted to understand why that happens and to build a portal that
            doesn&apos;t crash under the same load.
          </p>
          <cite className="block text-sm font-medium text-muted not-italic">
            Ahmed Ayyan, creator of RushDay
          </cite>
        </blockquote>
      </div>

      <Card>
        <CardHeader>
          <CardTitle>What actually happened</CardTitle>
        </CardHeader>
        <div className="max-w-3xl space-y-3 text-base leading-7 text-muted">
          <p>
            <strong className="text-text">The baseline.</strong> The first version of RushDay was
            built the way a lot of student projects are built: correct-looking code, no load
            testing, no thought given to what happens when hundreds of students act at once. It
            worked perfectly in every manual test.
          </p>
          <p>
            <strong className="text-text">Break it.</strong> A load test aimed at a single 30-place
            module handed out 154 places instead of 30, the database ran out of connections, and a
            fifth of the requests were refused before the server even saw them. A separate test
            found the dashboard&apos;s response time jumping from milliseconds to seconds between
            800 and 1,000 requests per second.
          </p>
          <p>
            <strong className="text-text">Fix it.</strong> Each failure got a real fix, in order: an
            atomic conditional update instead of a read-then-write race, a connection pool sized
            below what the database will actually grant, a bounded queue that sheds excess load with
            a fast "try again" instead of a slow failure for everyone, and fewer database round
            trips per page. Every fix is backed by an architecture decision record and a repeat of
            the same load test.
          </p>
        </div>
      </Card>

      {/* Busy only while loading: a region left aria-busy would keep announcing "Loading" and hide
          the machine banner from assistive technology after the results arrive. */}
      {results.isPending && (
        <LoadingRegion label="load-test results">
          <div className="space-y-4">
            <Skeleton className="h-16 w-full" />
            <Skeleton className="h-64 w-full" />
          </div>
        </LoadingRegion>
      )}
      {results.isError && (
        <Card>
          <p className="text-sm text-danger">
            Couldn&apos;t load the measured results right now. Reload the page to try again.
          </p>
        </Card>
      )}
      {results.machine && <MachineBanner machine={results.machine} />}

      {!results.isPending && !results.isError && (
        <div className="space-y-6">
          <EnrolmentRushChart />
          <DashboardKneeCharts />
          <ResultsDayTiles />
          <LoginStormTiles />
        </div>
      )}

      <Glossary />

      <Card>
        <CardHeader>
          <CardTitle>See it running</CardTitle>
          <CardDescription>
            Sign in with a demo account and try enrolling on a module or publishing results
            yourself.
          </CardDescription>
        </CardHeader>
        <div className="flex flex-wrap gap-3">
          <ButtonLink to="/login">Sign in</ButtonLink>
          <a
            href={githubUrl}
            target="_blank"
            rel="noreferrer noopener"
            className={buttonStyles({ variant: 'secondary' })}
          >
            <ExternalLink aria-hidden="true" className="size-4" />
            View the source on GitHub
          </a>
        </div>
      </Card>
    </div>
  )
}
