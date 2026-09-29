import type { FieldErrors, UseFormRegister } from 'react-hook-form'

import { FormField, Input, Select } from '@/components/ui'

import { LECTURER_TITLES, type LecturerFormValues } from '../lib/schemas'

/** Title, name, department and email: shared by CreateLecturerDialog and EditLecturerDialog. */
export function LecturerFields({
  register,
  errors,
}: {
  register: UseFormRegister<LecturerFormValues>
  errors: FieldErrors<LecturerFormValues>
}) {
  return (
    <>
      <div className="grid gap-4 sm:grid-cols-[8rem_1fr]">
        <FormField label="Title" required error={errors.title?.message}>
          <Select
            {...register('title')}
            options={LECTURER_TITLES.map((title) => ({ value: title, label: title }))}
          />
        </FormField>
        <FormField label="Full name" required error={errors.fullName?.message}>
          <Input {...register('fullName')} autoComplete="off" maxLength={200} />
        </FormField>
      </div>
      <FormField
        label="Department"
        required
        error={errors.department?.message}
        hint="The department code, for example CS, MA, PH or EE."
      >
        <Input
          {...register('department')}
          autoComplete="off"
          maxLength={8}
          className="max-w-32 font-mono uppercase"
        />
      </FormField>
      <FormField label="Email" error={errors.email?.message} hint="Optional.">
        <Input {...register('email')} type="email" autoComplete="off" maxLength={256} />
      </FormField>
    </>
  )
}
