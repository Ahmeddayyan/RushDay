import type { ComponentPropsWithRef, ReactNode } from 'react'

import { formatDateTime } from '@/lib/format'
import { zonedInputToMs } from '@/lib/zonedTime'

import { FormField } from './FormField'
import { Input } from './Input'

export interface ZonedDateTimeFieldProps {
  label: string
  timeZone: string
  /** The current `YYYY-MM-DDTHH:mm` value, for the "Reads as …" line. */
  value: string
  error?: string | undefined
  required?: boolean
  hint?: ReactNode
  inputProps: Omit<ComponentPropsWithRef<'input'>, 'type'>
}

/**
 * A `datetime-local` input whose value is wall-clock time in the institution's zone, with the zone
 * named in the label and the instant read back with its zone abbreviation ("Reads as 28 September
 * 2026 at 09:00 (BST)"), so an administrator or lecturer abroad never schedules for the wrong hour.
 */
export function ZonedDateTimeField({
  label,
  timeZone,
  value,
  error,
  required = false,
  hint,
  inputProps,
}: ZonedDateTimeFieldProps) {
  const ms = value ? zonedInputToMs(value, timeZone) : null
  return (
    <FormField
      label={`${label} (${timeZone})`}
      error={error}
      required={required}
      hint={
        hint || ms !== null ? (
          <>
            {hint}
            {hint && ms !== null ? ' ' : null}
            {ms !== null && <span>Reads as {formatDateTime(ms, timeZone)}.</span>}
          </>
        ) : undefined
      }
    >
      <Input type="datetime-local" {...inputProps} />
    </FormField>
  )
}
