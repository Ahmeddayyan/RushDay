import { useEffect } from 'react'

const appName = 'RushDay'

/** Sets document.title to "<title> · RushDay" while the calling page is mounted. */
export function usePageTitle(title?: string): void {
  useEffect(() => {
    document.title = title ? `${title} · ${appName}` : appName
    return () => {
      document.title = appName
    }
  }, [title])
}
