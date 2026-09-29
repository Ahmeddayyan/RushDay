import { useState } from 'react'

import { Button, FormField, Input, Select } from '@/components/ui'

import { AUDIT_ACTIONS } from '../lib/auditActions'
import {
  AUDIT_FILTER_KEYS,
  dayOf,
  dayStart,
  presetRange,
  type AuditFilterKey,
  type AuditFilterValues,
  type AuditRange,
} from '../lib/auditRange'
import type { UrlPatch } from '../lib/urlState'

const RANGES = [
  { value: '', label: 'Any time' },
  { value: 'today', label: 'Today' },
  { value: '7d', label: 'Last 7 days' },
  { value: '30d', label: 'Last 30 days' },
  { value: 'custom', label: 'Custom dates' },
]

/** A text filter that applies on Enter or when focus leaves it, so typing does not refetch. */
function TextFilter({
  label,
  value,
  placeholder,
  mono = false,
  upperCase = false,
  maxLength = 100,
  onApply,
}: {
  label: string
  value: string
  placeholder: string
  mono?: boolean
  upperCase?: boolean
  maxLength?: number
  onApply: (value: string) => void
}) {
  const [text, setText] = useState(value)
  const [seen, setSeen] = useState(value)
  // The URL changed from elsewhere (Clear filters, Back): show that value.
  if (seen !== value) {
    setSeen(value)
    setText(value)
  }
  function apply() {
    const next = upperCase ? text.trim().toUpperCase() : text.trim()
    if (next !== value) onApply(next)
  }
  return (
    <FormField label={label} className="w-full sm:w-44">
      <Input
        value={text}
        placeholder={placeholder}
        maxLength={maxLength}
        autoComplete="off"
        spellCheck={false}
        className={mono ? 'font-mono' : undefined}
        onChange={(event) => setText(event.target.value)}
        onBlur={apply}
        onKeyDown={(event) => {
          if (event.key === 'Enter') {
            event.preventDefault()
            apply()
          }
        }}
      />
    </FormField>
  )
}

/**
 * `AuditFilters` (05-frontend.md section 10, `/admin/audit`): actor, action from the catalogue,
 * student number, module code and a date range (Today, 7 days, 30 days or custom), all in the URL.
 * Presets are resolved to instants when chosen, so the URL names exactly what was asked for.
 */
export function AuditFilters({
  values,
  timeZone,
  now,
  onChange,
}: {
  values: AuditFilterValues
  timeZone: string
  now: () => number
  onChange: (patch: UrlPatch<AuditFilterKey>) => void
}) {
  const range = values.range as AuditRange
  const filtered = AUDIT_FILTER_KEYS.some((key) => values[key] !== '')

  function chooseRange(next: AuditRange) {
    if (next === '') onChange({ range: null, from: null, to: null })
    else if (next === 'custom') onChange({ range: 'custom' })
    else onChange({ range: next, ...presetRange(next, now(), timeZone) })
  }

  return (
    <div className="mb-4 flex flex-wrap items-end gap-3">
      <TextFilter
        label="Actor"
        value={values.actor}
        placeholder="Username"
        mono
        onApply={(actor) => onChange({ actor })}
      />
      <FormField label="Action" className="w-full sm:w-64">
        <Select
          value={values.action}
          onChange={(event) => onChange({ action: event.target.value })}
        >
          <option value="">All actions</option>
          {AUDIT_ACTIONS.map((entry) => (
            <option key={entry.action} value={entry.action}>
              {entry.label}
            </option>
          ))}
        </Select>
      </FormField>
      <TextFilter
        label="Student number"
        value={values.studentNumber}
        placeholder="S000001"
        mono
        upperCase
        maxLength={7}
        onApply={(studentNumber) => onChange({ studentNumber })}
      />
      <TextFilter
        label="Module code"
        value={values.moduleCode}
        placeholder="CS3099"
        mono
        upperCase
        maxLength={6}
        onApply={(moduleCode) => onChange({ moduleCode })}
      />
      <FormField label="Date range" className="w-full sm:w-44">
        <Select
          value={range}
          onChange={(event) => chooseRange(event.target.value as AuditRange)}
          options={RANGES}
        />
      </FormField>
      {range === 'custom' && (
        <>
          <FormField label={`From (${timeZone})`} className="w-full sm:w-48">
            <Input
              type="date"
              value={values.from ? dayOf(values.from, timeZone) : ''}
              onChange={(event) =>
                onChange({
                  from: event.target.value ? dayStart(event.target.value, timeZone) : null,
                })
              }
            />
          </FormField>
          <FormField label={`To (${timeZone}), inclusive`} className="w-full sm:w-48">
            <Input
              type="date"
              value={values.to ? dayOf(values.to, timeZone, -1) : ''}
              onChange={(event) =>
                onChange({
                  to: event.target.value ? dayStart(event.target.value, timeZone, 1) : null,
                })
              }
            />
          </FormField>
        </>
      )}
      {filtered && (
        <Button
          variant="ghost"
          onClick={() =>
            onChange({
              actor: null,
              action: null,
              studentNumber: null,
              moduleCode: null,
              range: null,
              from: null,
              to: null,
            })
          }
        >
          Clear filters
        </Button>
      )}
    </div>
  )
}
