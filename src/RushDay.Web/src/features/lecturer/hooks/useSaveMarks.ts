import { useMutation, useQueryClient } from '@tanstack/react-query'

import { saveMarks } from '@/api/endpoints/lecturer'
import { isApiError } from '@/api/client'
import { queryKeys } from '@/api/keys'
import { describeProblem, isProblem } from '@/api/problem'
import type { GradeOutcome } from '@/api/types/common'
import type { MarksRow, SaveMarksRowRequest } from '@/api/types/lecturer'

/** `PUT /api/lecturer/modules/{code}/marks` sends at most 500 rows per request (02-api.md section 8.4). */
const CHUNK_SIZE = 500

export interface DirtyMarkRow {
  studentNumber: string
  mark: number | null
  outcome: GradeOutcome
  /** The row's version when this edit started; null for a brand-new grade. */
  version: number | null
}

export interface SaveMarksOutcome {
  /** Rows the server accepted, as stored (fresh version, updatedAt, enteredBy). */
  savedRows: MarksRow[]
  savedStudentNumbers: string[]
  /** Rejected because another change landed first (09-frontend.md section 10): stay dirty, highlighted. */
  staleStudentNumbers: string[]
  /** Rejected because the student is no longer an active enrolment this year. */
  notEnrolledStudentNumbers: string[]
  /** A chunk-level error that was neither `stale-mark` nor `not-enrolled-students` (e.g. `module-locked`). */
  chunkError?: string
  chunkErrorStudentNumbers: string[]
  requested: number
}

function chunk<T>(items: T[], size: number): T[][] {
  const chunks: T[][] = []
  for (let index = 0; index < items.length; index += size) chunks.push(items.slice(index, index + size))
  return chunks
}

function studentNumbersOf(error: unknown): string[] {
  const value = isApiError(error) ? error.problem?.studentNumbers : undefined
  return Array.isArray(value) ? value.filter((item): item is string => typeof item === 'string') : []
}

/**
 * Saves only dirty rows, each with its `version`, as sequential `PUT`s of at most 500 rows
 * (02-api.md section 8.4). Every chunk is all-or-nothing; a chunk that fails leaves exactly its own
 * rows unsaved (the loop still sends the remaining chunks) so one contested row never blocks the rest
 * of a 600-row save. `stale-mark` and `not-enrolled-students` extensions say precisely which students
 * to highlight; any other error applies to the whole chunk.
 */
export function useSaveMarks(code: string) {
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: async (dirtyRows: DirtyMarkRow[]): Promise<SaveMarksOutcome> => {
      const outcome: SaveMarksOutcome = {
        savedRows: [],
        savedStudentNumbers: [],
        staleStudentNumbers: [],
        notEnrolledStudentNumbers: [],
        chunkErrorStudentNumbers: [],
        requested: dirtyRows.length,
      }

      for (const rows of chunk(dirtyRows, CHUNK_SIZE)) {
        const body: SaveMarksRowRequest[] = rows.map((row) => ({
          studentNumber: row.studentNumber,
          mark: row.mark,
          outcome: row.outcome,
          version: row.version,
        }))
        try {
          const response = await saveMarks(code, { rows: body })
          outcome.savedRows.push(...response.rows)
          outcome.savedStudentNumbers.push(...response.rows.map((row) => row.studentNumber))
        } catch (error) {
          if (isProblem(error, 'stale-mark')) {
            outcome.staleStudentNumbers.push(...studentNumbersOf(error))
          } else if (isProblem(error, 'not-enrolled-students')) {
            outcome.notEnrolledStudentNumbers.push(...studentNumbersOf(error))
          } else {
            outcome.chunkError = describeProblem(error, { code }).message
            outcome.chunkErrorStudentNumbers.push(...rows.map((row) => row.studentNumber))
          }
        }
      }

      await queryClient.invalidateQueries({ queryKey: queryKeys.lecturer.module(code) })
      return outcome
    },
  })
}
