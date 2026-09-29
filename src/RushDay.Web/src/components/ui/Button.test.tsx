import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it, vi } from 'vitest'

import { Button } from './Button'

describe('Button', () => {
  it('is a type="button" by default and runs its handler', async () => {
    const onClick = vi.fn()
    render(<Button onClick={onClick}>Save</Button>)
    const button = screen.getByRole('button', { name: 'Save' })
    expect(button).toHaveAttribute('type', 'button')
    await userEvent.click(button)
    expect(onClick).toHaveBeenCalledTimes(1)
  })

  it('while loading keeps its name and width, sets aria-busy and blocks clicks', async () => {
    const onClick = vi.fn()
    render(
      <Button loading onClick={onClick}>
        Sign in
      </Button>,
    )
    const button = screen.getByRole('button', { name: 'Sign in' })
    expect(button).toHaveAttribute('aria-busy', 'true')
    expect(button).toBeDisabled()
    // The label stays in the layout (opacity only) so the button keeps its width.
    expect(screen.getByText('Sign in')).toHaveClass('opacity-0')
    await userEvent.click(button)
    expect(onClick).not.toHaveBeenCalled()
  })

  it('names an icon-only button through its required aria-label', () => {
    render(
      <Button size="icon" aria-label="Close" variant="ghost">
        <svg aria-hidden="true" />
      </Button>,
    )
    expect(screen.getByRole('button', { name: 'Close' })).toBeInTheDocument()
  })

  it('applies the variant styles', () => {
    render(<Button variant="danger">Withdraw</Button>)
    expect(screen.getByRole('button', { name: 'Withdraw' })).toHaveClass('bg-danger')
  })
})
