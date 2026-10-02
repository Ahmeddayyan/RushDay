import { render } from '@testing-library/react'
import { describe, expect, it } from 'vitest'

import { useDocumentTitle } from './useDocumentTitle'

function Title({ title }: { title: string }) {
  useDocumentTitle(title)
  return null
}

describe('useDocumentTitle', () => {
  it('sets document.title to the given value', () => {
    render(<Title title="Results · RushDay" />)
    expect(document.title).toBe('Results · RushDay')
  })

  it('updates document.title when the value changes', () => {
    const { rerender } = render(<Title title="First · RushDay" />)
    expect(document.title).toBe('First · RushDay')
    rerender(<Title title="Second · RushDay" />)
    expect(document.title).toBe('Second · RushDay')
  })
})
