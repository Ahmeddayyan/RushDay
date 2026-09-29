import { useEffect, useId } from 'react'

/**
 * Unsaved-work registry (05-frontend.md section 5.1 step 4). A form with unsaved changes (the marks
 * grid, the announcement and settings forms) calls `useDirtyForm(isDirty)`; when a request answers
 * 401 while any registered form is dirty, AuthProvider opens ReauthDialog instead of sending the
 * person to /login, so the work on the page survives a session that timed out.
 *
 * Module state on purpose: the registry has one reader (AuthProvider) and many writers, and it must
 * not re-render anything when a form's dirtiness changes.
 */
const dirtyForms = new Set<string>()

export function useDirtyForm(isDirty: boolean): void {
  const id = useId()

  useEffect(() => {
    if (!isDirty) return
    dirtyForms.add(id)
    return () => {
      dirtyForms.delete(id)
    }
  }, [id, isDirty])
}

/** True while at least one mounted form has registered unsaved changes. */
export function hasDirtyForms(): boolean {
  return dirtyForms.size > 0
}
