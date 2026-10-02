import type { ReactNode } from 'react'
import { Link } from 'react-router'

import { Card, PageHeader, SupportLink } from '@/components/ui'
import { useDocumentTitle } from '@/lib/useDocumentTitle'

const STATEMENT_DATE = '29 September 2026'

function Section({ id, title, children }: { id: string; title: string; children: ReactNode }) {
  return (
    <section aria-labelledby={id} className="space-y-3">
      <h2 id={id} className="text-lg font-semibold text-text">
        {title}
      </h2>
      <div className="space-y-3 text-sm leading-relaxed text-text md:text-base [&_li]:ml-5 [&_li]:list-disc [&_ul]:space-y-1.5">
        {children}
      </div>
    </section>
  )
}

/**
 * `/accessibility` (05-frontend.md section 10): the public accessibility statement: conformance
 * target, how it is tested, known limitations, how to report a problem, and the statement date.
 */
export function Component() {
  useDocumentTitle('Accessibility statement · RushDay')

  return (
    <div className="max-w-3xl">
      <PageHeader
        title="Accessibility statement"
        description="RushDay should work for everyone who studies, teaches or runs a course, whatever device or assistive technology they use."
      />
      <Card className="space-y-8">
        <Section id="a11y-target" title="Conformance target">
          <p>
            RushDay aims to meet the Web Content Accessibility Guidelines (WCAG) 2.2 at level AA.
            That means, among other things:
          </p>
          <ul>
            <li>everything can be done with a keyboard alone, with a visible focus indicator;</li>
            <li>
              pages work with screen readers, at 200% zoom and on screens as narrow as 320 pixels;
            </li>
            <li>
              text meets a contrast ratio of at least 4.5:1 in both the light and the dark theme;
            </li>
            <li>
              colour is never the only way information is shown: every status also has an icon and
              words;
            </li>
            <li>animation is switched off when your device asks for reduced motion.</li>
          </ul>
        </Section>

        <Section id="a11y-testing" title="How we test">
          <ul>
            <li>
              Automated checks with axe-core (rules for WCAG 2.0, 2.1 and 2.2 A and AA) run against
              every page in both themes as part of the release pipeline; a serious or critical
              finding stops the release.
            </li>
            <li>
              The contrast of every colour pair used for text is checked by a unit test on each
              change.
            </li>
            <li>Sign-in, enrolment and marks entry are walked through with the keyboard only.</li>
            <li>Every page is checked on a 360 pixel wide phone screen for sideways scrolling.</li>
          </ul>
        </Section>

        <Section id="a11y-limitations" title="Known limitations">
          <ul>
            <li>
              Charts on the{' '}
              <Link to="/story" className="font-medium text-primary underline underline-offset-2">
                story page
              </Link>{' '}
              and the operations page are pictures of numbers; each one has a table view with
              exactly the same figures.
            </li>
            <li>
              There is no PDF export. The printable results summary uses your browser&apos;s print
              function, which can also save as PDF.
            </li>
          </ul>
        </Section>

        <Section id="a11y-contact" title="Reporting a problem">
          <p>
            If something in RushDay is hard or impossible to use, or you need information in a
            different format, <SupportLink />. Tell us the page and what you were trying to do; we
            aim to reply within five working days.
          </p>
          <p>
            If you are not happy with the response, you can contact the Equality Advisory and
            Support Service (EASS).
          </p>
        </Section>

        <Section id="a11y-date" title="About this statement">
          <p>This statement was prepared on {STATEMENT_DATE} and is reviewed with every release.</p>
        </Section>
      </Card>
    </div>
  )
}
