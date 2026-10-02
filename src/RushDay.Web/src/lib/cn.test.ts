import { describe, expect, it } from 'vitest'

import { cn } from './cn'

describe('cn', () => {
  it('joins class names and drops false, null and undefined (0 survives: it can be a valid class token)', () => {
    expect(cn('a', false, null, undefined, 'b')).toBe('a b')
    expect(cn('a', 0, 'b')).toBe('a 0 b')
  })

  it('flattens nested arrays', () => {
    expect(cn('a', ['b', ['c', false]])).toBe('a b c')
  })

  it('returns an empty string for nothing at all', () => {
    expect(cn()).toBe('')
  })
})
