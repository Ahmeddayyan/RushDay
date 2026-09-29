import { useId, useRef } from 'react'
import { Search, X } from 'lucide-react'

import { cn } from '@/lib/cn'

import { controlClassName } from './field'

export interface SearchInputProps {
  /** Always given; hidden visually unless `showLabel`. */
  label: string
  showLabel?: boolean
  value: string
  /** Called on every keystroke; debounce the query with `useDebouncedValue` (250 ms). */
  onChange: (value: string) => void
  placeholder?: string
  className?: string
  id?: string
  maxLength?: number
}

/** A `type="search"` box with a clear button; Escape also clears it. */
export function SearchInput({
  label,
  showLabel = false,
  value,
  onChange,
  placeholder,
  className,
  id,
  maxLength = 100,
}: SearchInputProps) {
  const autoId = useId()
  const inputId = id ?? `search-${autoId}`
  const inputRef = useRef<HTMLInputElement>(null)

  return (
    <div className={cn('flex min-w-0 flex-col gap-1.5', className)}>
      <label
        htmlFor={inputId}
        className={cn('text-sm font-medium text-text', !showLabel && 'sr-only')}
      >
        {label}
      </label>
      <div className="relative">
        <Search
          aria-hidden="true"
          className="pointer-events-none absolute top-1/2 left-3 size-4 -translate-y-1/2 text-muted"
        />
        <input
          ref={inputRef}
          id={inputId}
          type="search"
          value={value}
          maxLength={maxLength}
          placeholder={placeholder}
          autoComplete="off"
          spellCheck={false}
          onChange={(event) => onChange(event.target.value)}
          onKeyDown={(event) => {
            if (event.key === 'Escape' && value) {
              event.preventDefault()
              onChange('')
            }
          }}
          className={controlClassName(
            'h-10 pr-10 pl-9 pointer-coarse:h-11 [&::-webkit-search-cancel-button]:appearance-none',
          )}
        />
        {value && (
          <button
            type="button"
            aria-label="Clear search"
            onClick={() => {
              onChange('')
              inputRef.current?.focus()
            }}
            className="absolute inset-y-0 right-0 flex w-10 cursor-pointer items-center justify-center rounded-r-md text-muted hover:text-text"
          >
            <X aria-hidden="true" className="size-4" />
          </button>
        )}
      </div>
    </div>
  )
}
