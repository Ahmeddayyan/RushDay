import { useEffect } from 'react'

/**
 * Sets document.title. Callers pass the full title, e.g. useDocumentTitle('Results · RushDay')
 * (05-frontend.md section 7).
 */
export function useDocumentTitle(title: string): void {
  useEffect(() => {
    document.title = title
  }, [title])
}
