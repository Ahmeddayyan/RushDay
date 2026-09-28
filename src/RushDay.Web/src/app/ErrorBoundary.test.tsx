import { render, screen } from '@testing-library/react'
import { createMemoryRouter, RouterProvider } from 'react-router'
import { describe, expect, it, vi } from 'vitest'

import { ErrorBoundary } from './ErrorBoundary'

function Boom(): never {
  throw new Error('kaboom')
}

describe('ErrorBoundary', () => {
  it('renders a minimal shell around a thrown error', async () => {
    vi.spyOn(console, 'error').mockImplementation(() => {})
    const router = createMemoryRouter(
      [{ path: '/', element: <Boom />, errorElement: <ErrorBoundary /> }],
      { initialEntries: ['/'] },
    )

    render(<RouterProvider router={router} />)

    expect(await screen.findByRole('heading', { name: 'Something went wrong' })).toBeInTheDocument()
    expect(screen.getByText('Something went wrong.')).toBeInTheDocument()
  })
})
