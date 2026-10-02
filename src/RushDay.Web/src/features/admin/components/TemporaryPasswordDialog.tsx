import { useState } from 'react'
import { Check, Copy, TriangleAlert } from 'lucide-react'

import { Button, Dialog, DialogContent, DialogFooter } from '@/components/ui'

export interface TemporaryPasswordDialogProps {
  /** Null closes the dialog; the password is never kept after that. */
  credentials: { username: string; password: string; isDemo?: boolean } | null
  onClose: () => void
  /** The administrator reset their own password: the session ends after this dialog. */
  selfReset?: boolean
}

/**
 * The temporary password, shown once (02-api.md section 8.5, provision and reset password), with
 * Copy. Clicking outside does not close it, so the password cannot be lost by a stray click.
 */
export function TemporaryPasswordDialog({
  credentials,
  onClose,
  selfReset = false,
}: TemporaryPasswordDialogProps) {
  const [copy, setCopy] = useState<'idle' | 'copied' | 'failed'>('idle')

  async function copyPassword() {
    if (!credentials) return
    try {
      await navigator.clipboard.writeText(credentials.password)
      setCopy('copied')
    } catch {
      setCopy('failed')
    }
  }

  return (
    <Dialog
      open={credentials !== null}
      onOpenChange={(open) => {
        if (!open) {
          setCopy('idle')
          onClose()
        }
      }}
    >
      {credentials && (
        <DialogContent
          title={`Temporary password for ${credentials.username}`}
          description="Give this to the person through a channel only they can read."
          onInteractOutside={(event) => event.preventDefault()}
        >
          <div className="flex flex-col gap-4">
            <p className="-mb-2 text-sm font-medium text-text">Temporary password</p>
            <div className="flex flex-wrap items-center gap-2 rounded-md border border-border bg-surface-2 p-3">
              <code
                data-testid="temporary-password"
                className="min-w-0 flex-1 font-mono text-lg break-all text-text select-all"
              >
                {credentials.password}
              </code>
              <Button variant="secondary" size="sm" onClick={() => void copyPassword()}>
                {copy === 'copied' ? (
                  <Check aria-hidden="true" className="size-4" />
                ) : (
                  <Copy aria-hidden="true" className="size-4" />
                )}
                {copy === 'copied' ? 'Copied' : 'Copy'}
              </Button>
            </div>
            <p role="status" className="text-sm text-muted">
              {copy === 'copied' && 'Copied to the clipboard.'}
              {copy === 'failed' && 'Copying is blocked here. Select the password and copy it.'}
            </p>
            {credentials.isDemo ? (
              <p className="text-sm text-text">
                This won&apos;t be shown again. This is a demo account: it can&apos;t change its
                password, and it is disabled with the rest of the demo when demo mode is switched
                off.
              </p>
            ) : (
              <p className="text-sm text-text">
                This won&apos;t be shown again. The person must change it and, for administrators,
                set up two-step verification at first sign-in.
              </p>
            )}
            {selfReset && (
              <p className="flex items-start gap-2 rounded-md border border-warning/30 bg-warning-soft px-3 py-2 text-sm text-warning">
                <TriangleAlert aria-hidden="true" className="mt-0.5 size-4 shrink-0" />
                You&apos;ll be signed out after copying the temporary password.
              </p>
            )}
          </div>
          <DialogFooter>
            <Button
              onClick={() => {
                setCopy('idle')
                onClose()
              }}
            >
              {selfReset ? 'Done, sign me out' : 'Done'}
            </Button>
          </DialogFooter>
        </DialogContent>
      )}
    </Dialog>
  )
}
