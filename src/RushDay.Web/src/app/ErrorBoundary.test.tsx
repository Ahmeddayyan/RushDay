import { render, screen } from '@testing-library/react'
import { createMemoryRouter, data, RouterProvider } from 'react-router'
import { describe, expect, it, vi } from 'vitest'

import { ErrorBoundary } from './ErrorBoundary'

function Boom({ error }: { error: unknown }): never {
  throw error
}

function renderThrowing(error: unknown) {
  vi.spyOn(console, 'error').mockImplementation(() => {})
  vi.spyOn(console, 'warn').mockImplementation(() => {})
  const router = createMemoryRouter([
    { path: '/', element: <Boom error={error} />, errorElement: <ErrorBoundary /> },
  ])
  render(<RouterProvider router={router} />)
}

describe('ErrorBoundary', () => {
  it('replaces a crashed page with a calm message and a way forward', async () => {
    renderThrowing(new Error('kaboom'))
    expect(await screen.findByRole('heading', { name: 'Something went wrong' })).toBeInTheDocument()
    expect(screen.getByText('Something went wrong while showing this page.')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Reload' })).toBeInTheDocument()
    expect(screen.getByRole('link', { name: 'Go to your home page' })).toHaveAttribute('href', '/')
  })

  it('asks for a reload when a lazy chunk is gone after a deploy', async () => {
    renderThrowing(
      new TypeError('Failed to fetch dynamically imported module: /assets/LoginPage-abc.js'),
    )
    expect(
      await screen.findByText(/RushDay has been updated since this page was opened/),
    ).toBeInTheDocument()
  })

  it('names a route error response', async () => {
    vi.spyOn(console, 'error').mockImplementation(() => {})
    const router = createMemoryRouter([
      {
        path: '/',
        loader: () => {
          // React Router's idiom for an HTTP-style route error is to throw its data() response.
          // eslint-disable-next-line @typescript-eslint/only-throw-error
          throw data('missing', { status: 404, statusText: 'Not Found' })
        },
        element: <p>never</p>,
        errorElement: <ErrorBoundary />,
      },
    ])
    render(<RouterProvider router={router} />)
    expect(await screen.findByText('The page answered 404 Not Found.')).toBeInTheDocument()
  })
})
