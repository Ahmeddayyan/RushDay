import { render, screen } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'

import { Providers } from './providers'

describe('Providers', () => {
  it('wraps children with the query client and auth provider', () => {
    vi.stubGlobal(
      'fetch',
      vi.fn<typeof fetch>(() => new Promise<Response>(() => {})),
    )

    render(
      <Providers>
        <p>hello</p>
      </Providers>,
    )

    expect(screen.getByText('hello')).toBeInTheDocument()
  })
})
