import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it, vi } from 'vitest'

import { Pagination } from './Pagination'

describe('Pagination', () => {
  it('pages through and says what is on screen', async () => {
    const onPageChange = vi.fn()
    render(
      <Pagination
        page={2}
        pageSize={25}
        total={60}
        onPageChange={onPageChange}
        itemLabel="students"
      />,
    )
    expect(screen.getByText('Showing 26–50 of 60 students')).toBeInTheDocument()
    await userEvent.click(screen.getByRole('button', { name: 'Next page' }))
    expect(onPageChange).toHaveBeenCalledWith(3)
  })

  it('stops at row 10,000, where the API stops, and says how to reach the rest', () => {
    render(
      <Pagination
        page={400}
        pageSize={25}
        total={20_041}
        onPageChange={() => {}}
        itemLabel="accounts"
      />,
    )
    expect(screen.getByRole('button', { name: 'Next page' })).toBeDisabled()
    expect(screen.getByRole('button', { name: 'Previous page' })).toBeEnabled()
    expect(
      screen.getByText(
        'Paging stops at the first 10,000 accounts. Narrow the search or the filters to reach the rest.',
      ),
    ).toBeInTheDocument()
  })

  it('does not mention the limit before it is reached', () => {
    render(<Pagination page={399} pageSize={25} total={20_041} onPageChange={() => {}} />)
    expect(screen.getByRole('button', { name: 'Next page' })).toBeEnabled()
    expect(screen.queryByText(/Paging stops/)).not.toBeInTheDocument()
  })
})
