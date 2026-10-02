import { act, renderHook } from '@testing-library/react'
import { http, HttpResponse } from 'msw'
import { afterEach, describe, expect, it, vi } from 'vitest'

import { server } from '@/test/server'

import { openAuthChannel } from './broadcast'
import { downloadBlob, downloadFromApi, downloadText, filenameFromDisposition } from './download'
import {
  capitalise,
  formatDate,
  formatDateTime,
  formatDecimal,
  formatNumber,
  formatRelative,
  formatRole,
  formatSemester,
  formatShortDate,
  formatTime,
  supportLink,
  supportText,
  zoneLabel,
} from './format'
import {
  departmentName,
  departmentOf,
  isModuleCode,
  levelOf,
  normaliseModuleCode,
  parseModuleCode,
} from './moduleCode'
import { qrDataUrl } from './qr'
import { useDebouncedValue } from './useDebouncedValue'
import { hasDirtyForms, useDirtyForm } from './useDirtyForm'

vi.mock('qrcode', () => ({
  toDataURL: vi.fn((text: string) => Promise.resolve(`data:image/png;base64,${text.length}`)),
}))

afterEach(() => {
  vi.useRealTimers()
})

describe('format', () => {
  it('formats instants in the institution zone, naming the zone', () => {
    expect(formatDate('2026-09-28T09:00:00Z')).toBe('28 September 2026')
    expect(formatShortDate('2026-01-26T09:00:00Z')).toBe('26 Jan 2026')
    expect(formatTime('2026-09-28T09:00:00Z')).toBe('10:00')
    expect(zoneLabel('2026-09-28T09:00:00Z')).toBe('BST')
    expect(zoneLabel('2026-01-26T09:00:00Z')).toBe('GMT')
    expect(formatDateTime('2026-01-26T09:00:00Z')).toBe('26 January 2026 at 09:00 (GMT)')
    expect(formatDateTime('2026-01-26T09:00:00Z', 'Europe/London', { zone: false })).toBe(
      '26 January 2026 at 09:00',
    )
    expect(formatTime('2026-01-26T09:00:00Z', 'Asia/Tokyo')).toBe('18:00')
  })

  it('falls back to the default zone for an unknown zone id', () => {
    expect(formatTime('2026-01-26T09:00:00Z', 'Not/AZone')).toBe('09:00')
  })

  it('formats relative times', () => {
    const now = Date.parse('2026-09-28T09:00:00Z')
    expect(formatRelative('2026-09-28T09:00:20Z', now)).toBe('just now')
    expect(formatRelative('2026-09-28T11:00:00Z', now)).toBe('in 2 hours')
    expect(formatRelative('2026-09-25T09:00:00Z', now)).toBe('3 days ago')
    expect(formatRelative(new Date('2027-09-28T09:00:00Z'), now)).toBe('next year')
  })

  it('formats numbers, semesters, roles and sentences', () => {
    expect(formatNumber(20000)).toBe('20,000')
    expect(formatDecimal(70)).toBe('70.0')
    expect(formatSemester('autumn')).toBe('Autumn')
    expect(formatSemester('spring')).toBe('Spring')
    expect(formatRole('Admin')).toBe('Administrator')
    expect(formatRole('Lecturer')).toBe('Lecturer')
    expect(capitalise('contact')).toBe('Contact')
    expect(capitalise('')).toBe('')
  })

  it('writes the support sentence from the institution settings', () => {
    expect(supportText(null)).toBe('contact the academic office')
    expect(supportText({ email: 'registry@example.ac.uk', url: 'https://x' })).toBe(
      'contact the academic office at registry@example.ac.uk',
    )
    expect(supportText({ email: null, url: 'https://help.example.ac.uk' })).toBe(
      'contact the academic office (https://help.example.ac.uk)',
    )
    expect(supportLink(undefined)).toBe('contact the academic office')
  })
})

describe('moduleCode', () => {
  it('parses department and level from the code', () => {
    expect(parseModuleCode('cs3099 ')).toEqual({ department: 'CS', level: 3, number: '3099' })
    expect(parseModuleCode('CS30')).toBeNull()
    expect(departmentOf('MA1001')).toBe('MA')
    expect(levelOf('PH2001')).toBe(2)
    expect(levelOf('nope')).toBeNull()
    expect(departmentOf('nope')).toBeNull()
    expect(isModuleCode('EE1001')).toBe(true)
    expect(normaliseModuleCode(' ee1001')).toBe('EE1001')
    expect(departmentName('CS')).toBe('Computer Science')
    expect(departmentName('XX')).toBe('XX')
  })
})

describe('download', () => {
  it('reads the file name from Content-Disposition', () => {
    expect(
      filenameFromDisposition(
        new Headers({ 'Content-Disposition': 'attachment; filename="audit.csv"' }),
        'x',
      ),
    ).toBe('audit.csv')
    expect(
      filenameFromDisposition(
        new Headers({
          'Content-Disposition': "attachment; filename*=UTF-8''r%C3%A9sum%C3%A9.json",
        }),
        'x',
      ),
    ).toBe('résumé.json')
    expect(
      filenameFromDisposition(
        new Headers({ 'Content-Disposition': "attachment; filename*=UTF-8''%E0%A4%A" }),
        'fallback.csv',
      ),
    ).toBe('fallback.csv')
    expect(filenameFromDisposition(new Headers(), 'fallback.csv')).toBe('fallback.csv')
  })

  it('saves blobs, text and API attachments through a temporary link', async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true })
    const createObjectURL = vi.fn(() => 'blob:x')
    const revokeObjectURL = vi.fn()
    Object.defineProperty(URL, 'createObjectURL', { configurable: true, value: createObjectURL })
    Object.defineProperty(URL, 'revokeObjectURL', { configurable: true, value: revokeObjectURL })
    const names: string[] = []
    vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(function (
      this: HTMLAnchorElement,
    ) {
      names.push(this.download)
    })

    downloadBlob(new Blob(['a']), 'a.txt')
    downloadText('BEGIN:VCALENDAR', 'timetable.ics', 'text/calendar')
    server.use(
      http.get('/api/me/export.json', () =>
        HttpResponse.json(
          {},
          { headers: { 'Content-Disposition': 'attachment; filename="rushday-S000001.json"' } },
        ),
      ),
    )
    const headers = await downloadFromApi('/api/me/export.json', 'export.json')
    expect(headers.get('Content-Disposition')).toContain('rushday-S000001.json')

    expect(names).toEqual(['a.txt', 'timetable.ics', 'rushday-S000001.json'])
    await vi.advanceTimersByTimeAsync(1)
    expect(revokeObjectURL).toHaveBeenCalledTimes(3)
    expect(document.querySelectorAll('a[download]')).toHaveLength(0)
  })
})

describe('broadcast', () => {
  it('delivers logout messages between tabs and ignores anything else', async () => {
    const received: unknown[] = []
    const listener = openAuthChannel((message) => received.push(message))
    const sender = openAuthChannel(() => {})
    sender.post({ type: 'logout' })
    const raw = new BroadcastChannel('rushday.auth')
    raw.postMessage({ type: 'something-else' })
    await vi.waitFor(() => expect(received).toEqual([{ type: 'logout' }]))
    listener.close()
    sender.close()
    raw.close()
    // Posting on a closed channel is harmless.
    sender.post({ type: 'logout' })
  })

  it('is a no-op where BroadcastChannel is missing', () => {
    vi.stubGlobal('BroadcastChannel', undefined)
    const channel = openAuthChannel(() => {})
    expect(() => {
      channel.post({ type: 'logout' })
      channel.close()
    }).not.toThrow()
  })

  it('is a no-op when BroadcastChannel cannot be constructed', () => {
    vi.stubGlobal(
      'BroadcastChannel',
      vi.fn(() => {
        throw new Error('blocked')
      }),
    )
    const channel = openAuthChannel(() => {})
    expect(() => channel.post({ type: 'logout' })).not.toThrow()
  })
})

describe('qr', () => {
  it('draws the QR code with the lazily loaded qrcode package', async () => {
    await expect(qrDataUrl('otpauth://totp/x')).resolves.toBe('data:image/png;base64,16')
  })
})

describe('useDebouncedValue', () => {
  it('returns the value once it stops changing', () => {
    vi.useFakeTimers()
    const { result, rerender } = renderHook(({ value }) => useDebouncedValue(value, 250), {
      initialProps: { value: 'c' },
    })
    rerender({ value: 'cs' })
    rerender({ value: 'cs3' })
    expect(result.current).toBe('c')
    act(() => {
      vi.advanceTimersByTime(249)
    })
    expect(result.current).toBe('c')
    act(() => {
      vi.advanceTimersByTime(1)
    })
    expect(result.current).toBe('cs3')
  })
})

describe('useDirtyForm', () => {
  it('registers unsaved work while mounted and dirty', () => {
    const { rerender, unmount } = renderHook(({ dirty }) => useDirtyForm(dirty), {
      initialProps: { dirty: false },
    })
    expect(hasDirtyForms()).toBe(false)
    rerender({ dirty: true })
    expect(hasDirtyForms()).toBe(true)
    rerender({ dirty: false })
    expect(hasDirtyForms()).toBe(false)
    rerender({ dirty: true })
    unmount()
    expect(hasDirtyForms()).toBe(false)
  })
})
