import type { FieldErrors, UseFormRegister } from 'react-hook-form'

import { FormField, Input } from '@/components/ui'

import type { StudentFormValues } from '../lib/schemas'

/** Name, programme, year and email: shared by CreateStudentDialog and EditStudentDialog. */
export function StudentFields({
  register,
  errors,
}: {
  register: UseFormRegister<StudentFormValues>
  errors: FieldErrors<StudentFormValues>
}) {
  return (
    <>
      <FormField label="Full name" required error={errors.fullName?.message}>
        <Input {...register('fullName')} autoComplete="off" maxLength={200} />
      </FormField>
      <FormField label="Programme" required error={errors.programme?.message}>
        <Input
          {...register('programme')}
          autoComplete="off"
          maxLength={200}
          placeholder="BSc Computer Science"
        />
      </FormField>
      <FormField label="Year of study" required error={errors.yearOfStudy?.message} hint="1 to 6.">
        <Input
          {...register('yearOfStudy')}
          type="text"
          inputMode="numeric"
          pattern="[0-9]*"
          maxLength={1}
          className="max-w-24"
        />
      </FormField>
      <FormField
        label="Email"
        error={errors.email?.message}
        hint="Optional. Used on the account if one is provisioned."
      >
        <Input {...register('email')} type="email" autoComplete="off" maxLength={256} />
      </FormField>
    </>
  )
}
