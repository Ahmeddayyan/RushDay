import { useId, useState, type KeyboardEvent, type ReactNode } from 'react'
import { Check, ChevronDown } from 'lucide-react'

import { cn } from '@/lib/cn'

import { controlClassName, useFieldControl } from './field'
import { Spinner } from './Spinner'

export interface ComboboxProps<T> {
  /** The options for the current `inputValue`, usually the result of a server search with `q`. */
  items: readonly T[]
  getKey: (item: T) => string
  /** The text that goes into the input when an option is chosen. */
  getLabel: (item: T) => string
  renderItem?: (item: T) => ReactNode
  value: T | null
  onChange: (item: T | null) => void
  inputValue: string
  onInputChange: (value: string) => void
  loading?: boolean
  /** Shown when the popup is open, nothing is loading and there are no items. */
  emptyState?: ReactNode
  placeholder?: string
  disabled?: boolean
  id?: string
  name?: string
  'aria-describedby'?: string
}

/**
 * ARIA 1.2 combobox with a listbox popup (05-frontend.md section 9.3): the input keeps focus, the
 * active option is announced through `aria-activedescendant`, typing asks the caller for new items
 * (`onInputChange`, which usually drives a debounced server `q`), and an empty-state slot explains
 * why nothing matches. Arrow keys move, Enter chooses, Escape closes.
 */
export function Combobox<T>({
  items,
  getKey,
  getLabel,
  renderItem,
  value,
  onChange,
  inputValue,
  onInputChange,
  loading = false,
  emptyState,
  placeholder,
  disabled = false,
  id,
  name,
  'aria-describedby': describedBy,
}: ComboboxProps<T>) {
  const autoId = useId()
  const field = useFieldControl({ id, 'aria-describedby': describedBy })
  const inputId = field.id ?? `combobox-${autoId}`
  const listId = `${inputId}-listbox`
  const [open, setOpen] = useState(false)
  const [activeIndex, setActiveIndex] = useState(-1)

  const optionId = (index: number) => `${inputId}-option-${index}`
  const showList = open && !disabled
  const active = activeIndex >= 0 && activeIndex < items.length ? activeIndex : -1

  function choose(item: T) {
    onChange(item)
    onInputChange(getLabel(item))
    setOpen(false)
    setActiveIndex(-1)
  }

  function onKeyDown(event: KeyboardEvent<HTMLInputElement>) {
    switch (event.key) {
      case 'ArrowDown': {
        event.preventDefault()
        setOpen(true)
        setActiveIndex((index) => (items.length === 0 ? -1 : (index + 1) % items.length))
        break
      }
      case 'ArrowUp': {
        event.preventDefault()
        setOpen(true)
        setActiveIndex((index) =>
          items.length === 0 ? -1 : index <= 0 ? items.length - 1 : index - 1,
        )
        break
      }
      case 'Home':
      case 'End': {
        if (!showList || items.length === 0) return
        event.preventDefault()
        setActiveIndex(event.key === 'Home' ? 0 : items.length - 1)
        break
      }
      case 'Enter': {
        const item = active >= 0 ? items[active] : undefined
        if (showList && item !== undefined) {
          event.preventDefault()
          choose(item)
        }
        break
      }
      case 'Escape': {
        if (showList) {
          event.preventDefault()
          setOpen(false)
          setActiveIndex(-1)
        } else if (inputValue) {
          onInputChange('')
          onChange(null)
        }
        break
      }
      default:
        break
    }
  }

  return (
    <div className="relative">
      <input
        id={inputId}
        name={name}
        type="text"
        role="combobox"
        aria-expanded={showList}
        aria-controls={listId}
        aria-autocomplete="list"
        aria-activedescendant={showList && active >= 0 ? optionId(active) : undefined}
        aria-describedby={field['aria-describedby']}
        aria-invalid={field['aria-invalid']}
        aria-required={field['aria-required']}
        autoComplete="off"
        placeholder={placeholder}
        disabled={disabled}
        value={inputValue}
        onChange={(event) => {
          onInputChange(event.target.value)
          if (value !== null) onChange(null)
          setOpen(true)
          setActiveIndex(-1)
        }}
        onFocus={() => setOpen(true)}
        onBlur={() => setOpen(false)}
        onKeyDown={onKeyDown}
        className={controlClassName('h-10 pr-9 pointer-coarse:h-11')}
      />
      <ChevronDown
        aria-hidden="true"
        className="pointer-events-none absolute top-1/2 right-3 size-4 -translate-y-1/2 text-muted"
      />
      {showList && (
        <div className="absolute inset-x-0 top-full z-50 mt-1 overflow-hidden rounded-md border border-border bg-surface shadow-overlay">
          <ul
            id={listId}
            role="listbox"
            aria-label="Suggestions"
            className="max-h-64 overflow-y-auto py-1"
          >
            {items.map((item, index) => {
              const selected = value !== null && getKey(value) === getKey(item)
              return (
                <li
                  key={getKey(item)}
                  id={optionId(index)}
                  role="option"
                  aria-selected={index === active}
                  // mousedown, not click: the input must not blur (and close the list) first.
                  onMouseDown={(event) => {
                    event.preventDefault()
                    choose(item)
                  }}
                  onMouseEnter={() => setActiveIndex(index)}
                  className={cn(
                    'flex cursor-pointer items-center gap-2 px-3 py-2 text-sm text-text',
                    index === active && 'bg-primary-soft',
                  )}
                >
                  <Check
                    aria-hidden="true"
                    className={cn('size-4 shrink-0 text-primary', !selected && 'invisible')}
                  />
                  <span className="min-w-0 flex-1">
                    {renderItem ? renderItem(item) : getLabel(item)}
                  </span>
                </li>
              )
            })}
          </ul>
          {loading && (
            <div className="px-3 py-2">
              <Spinner size="sm" label="Searching" />
            </div>
          )}
          {!loading && items.length === 0 && (
            <div role="status" className="px-3 py-3 text-sm text-muted">
              {emptyState ?? 'No matches.'}
            </div>
          )}
        </div>
      )}
    </div>
  )
}
