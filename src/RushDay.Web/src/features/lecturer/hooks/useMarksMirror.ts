import { useEffect } from 'react'

import type { GradeOutcome } from '@/api/types/common'

/**
 * Mirrors unsaved marks-grid rows to `sessionStorage` (05-frontend.md section 10, the Marks tab):
 * every change is written under `rushday.marks.{code}`; a session that ends mid-edit (an expired
 * cookie ReauthDialog could not recover, a crashed tab) still has its edits on the next visit. Reads
 * and writes are wrapped in try/catch: a private window or a full storage quota must never break the
 * grid, only lose the safety net.
 */

export interface MirroredMarkRow {
  mark: number | null
  outcome: GradeOutcome
  /** The row's version when the edit started, so a save can still detect a stale conflict. */
  version: number | null
}

export type MarksMirror = Record<string, MirroredMarkRow>

function mirrorKey(code: string): string {
  return `rushday.marks.${code}`
}

/** Reads the mirror once (on mount); null when there is nothing to restore. */
export function readMarksMirror(code: string): MarksMirror | null {
  try {
    const raw = sessionStorage.getItem(mirrorKey(code))
    if (!raw) return null
    const parsed = JSON.parse(raw) as MarksMirror
    return Object.keys(parsed).length > 0 ? parsed : null
  } catch {
    return null
  }
}

/** Clears the mirror (a successful save, or after it has been restored into the grid). */
export function clearMarksMirror(code: string): void {
  try {
    sessionStorage.removeItem(mirrorKey(code))
  } catch {
    // Nothing to clean up if storage was never reachable.
  }
}

/** Writes `dirty` to the mirror on every change; an empty object clears it. */
export function useMarksMirror(code: string, dirty: MarksMirror): void {
  useEffect(() => {
    try {
      if (Object.keys(dirty).length === 0) sessionStorage.removeItem(mirrorKey(code))
      else sessionStorage.setItem(mirrorKey(code), JSON.stringify(dirty))
    } catch {
      // The grid still holds the edits in memory; only the cross-session safety net is lost.
    }
  }, [code, dirty])
}
