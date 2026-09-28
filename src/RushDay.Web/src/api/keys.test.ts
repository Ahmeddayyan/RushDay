import { describe, expect, it } from 'vitest'

import { queryKeys } from './keys'

describe('queryKeys', () => {
  it('exposes the keys stage S3 needs', () => {
    expect(queryKeys.index).toEqual(['index'])
    expect(queryKeys.publicStatus).toEqual(['public', 'status'])
    expect(queryKeys.authMe).toEqual(['auth', 'me'])
  })
})
