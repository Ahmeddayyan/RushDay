import { render, screen, waitFor } from '@testing-library/react'
import { describe, expect, it } from 'vitest'

import { createTestQueryClient } from '@/test/render'

import { useAuth } from './AuthProvider'
import { Providers } from './providers'

function Status() {
  return <p>{useAuth().status}</p>
}

describe('Providers', () => {
  it('wraps children with the query client, the session and the toast outlet', async () => {
    render(
      <Providers client={createTestQueryClient()}>
        <Status />
      </Providers>,
    )
    expect(screen.getByText('booting')).toBeInTheDocument()
    await waitFor(() => expect(screen.getByText('anonymous')).toBeInTheDocument())
    expect(screen.getByLabelText(/Notifications/)).toBeInTheDocument()
  })

  it('uses the application query client by default', () => {
    render(
      <Providers>
        <p>hello</p>
      </Providers>,
    )
    expect(screen.getByText('hello')).toBeInTheDocument()
  })
})
