import { screen, within } from '@testing-library/react'
import { describe, expect, it } from 'vitest'

import { loadResultsHandler, makeLoadResults } from '@/test/handlers/ops'
import { renderRoutes } from '@/test/render'

import { storyRoutes } from './routes'

// This route's lazy chunk pulls in all four load-story charts (Recharts is heavier to evaluate
// than a plain page module), which is slower under a full, loaded test run: a generous find
// timeout, and a matching per-test timeout, keep this test from flaking on a busy machine.
const FIND_OPTIONS = { timeout: 8000 }

describe('StoryPage (/story, public)', () => {
  it('renders anonymously, with the story sentence pair verbatim, the machine banner, chart cards and the glossary', async () => {
    renderRoutes(storyRoutes, {
      route: '/story',
      user: null,
      handlers: [loadResultsHandler(makeLoadResults())],
    })

    expect(
      await screen.findByText(
        "I built RushDay because my own university's portal fell over every results day and enrolment window. I wanted to understand why that happens and to build a portal that doesn't crash under the same load.",
        {},
        FIND_OPTIONS,
      ),
    ).toBeInTheDocument()
    expect(screen.getByText('Ahmed Ayyan, creator of RushDay')).toBeInTheDocument()

    expect(
      await screen.findByText(/Measured on Developer laptop/, {}, FIND_OPTIONS),
    ).toBeInTheDocument()
    expect(screen.getByText('The enrolment rush')).toBeInTheDocument()
    expect(screen.getByText('Where the dashboard breaks')).toBeInTheDocument()
    expect(screen.getByText('Results day')).toBeInTheDocument()
    expect(screen.getByText('The login storm')).toBeInTheDocument()

    expect(screen.getByText('Terms used on this page')).toBeInTheDocument()
    expect(screen.getByText('Oversold')).toBeInTheDocument()

    const main = within(screen.getByRole('main'))
    expect(main.getByRole('link', { name: 'Sign in' })).toHaveAttribute('href', '/login')
    expect(main.getByRole('link', { name: /View the source on GitHub/ })).toHaveAttribute(
      'href',
      'https://github.com/Ahmeddayyan/RushDay',
    )
  }, 15_000)
})
