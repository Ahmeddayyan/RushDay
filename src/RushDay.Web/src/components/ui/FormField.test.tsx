import { render, screen } from '@testing-library/react'
import { describe, expect, it } from 'vitest'

import { FormField } from './FormField'
import { Input } from './Input'
import { PasswordInput } from './PasswordInput'
import { Select } from './Select'
import { Textarea } from './Textarea'

describe('FormField', () => {
  it('labels the control and links the hint through aria-describedby', () => {
    render(
      <FormField label="Student number" hint="Six digits after the S.">
        <Input />
      </FormField>,
    )
    const input = screen.getByLabelText('Student number')
    expect(input.tagName).toBe('INPUT')
    expect(input).toHaveAccessibleDescription('Six digits after the S.')
    expect(input).not.toHaveAttribute('aria-invalid')
  })

  it('marks the control invalid and describes it with the error', () => {
    render(
      <FormField label="Mark" hint="0 to 100." error="Enter a whole number from 0 to 100.">
        <Input inputMode="numeric" />
      </FormField>,
    )
    const input = screen.getByLabelText('Mark')
    expect(input).toHaveAttribute('aria-invalid', 'true')
    expect(input).toHaveAccessibleDescription('0 to 100. Enter a whole number from 0 to 100.')
  })

  it('says "required" in words and sets aria-required', () => {
    render(
      <FormField label="Reason" required>
        <Textarea />
      </FormField>,
    )
    const textarea = screen.getByRole('textbox', { name: /Reason/ })
    expect(textarea).toHaveAccessibleName('Reason (required)')
    expect(textarea).toHaveAttribute('aria-required', 'true')
  })

  it('gives role="alert" to the first error of a form only', () => {
    render(
      <form>
        <FormField label="Username" error="Enter your username.">
          <Input />
        </FormField>
        <FormField label="Password" error="Enter your password.">
          <PasswordInput autoComplete="current-password" />
        </FormField>
      </form>,
    )
    const alerts = screen.getAllByRole('alert')
    expect(alerts).toHaveLength(1)
    expect(alerts[0]).toHaveTextContent('Enter your username.')
    expect(screen.getByLabelText('Password')).toHaveAccessibleDescription('Enter your password.')
  })

  it('wires a select the same way', () => {
    render(
      <FormField label="Semester" error="Choose a semester.">
        <Select
          options={[
            { value: 'autumn', label: 'Autumn' },
            { value: 'spring', label: 'Spring' },
          ]}
        />
      </FormField>,
    )
    const select = screen.getByRole('combobox', { name: 'Semester' })
    expect(select).toHaveAttribute('aria-invalid', 'true')
    expect(screen.getAllByRole('option')).toHaveLength(2)
  })

  it('can hide its label visually and still name the control', () => {
    render(
      <FormField label="Search modules" hideLabel>
        <Input type="search" />
      </FormField>,
    )
    expect(screen.getByRole('searchbox', { name: 'Search modules' })).toBeInTheDocument()
    expect(screen.getByText('Search modules')).toHaveClass('sr-only')
  })
})
