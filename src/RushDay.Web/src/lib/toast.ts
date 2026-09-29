import { createElement, type ReactNode } from 'react'
import { toast as sonner, type ExternalToast } from 'sonner'

/**
 * Toasts with the live-region roles of 05-frontend.md section 12: success and info render inside
 * `role="status"` (polite), errors inside `role="alert"` (assertive). Sonner's own container is a
 * polite live region with no per-toast role, so the role goes on the message itself. Use this module
 * instead of importing `toast` from sonner directly.
 */

function withRole(role: 'status' | 'alert', message: ReactNode): ReactNode {
  return createElement('span', { role }, message)
}

export const toast = {
  success(message: ReactNode, options?: ExternalToast) {
    return sonner.success(withRole('status', message), options)
  },
  info(message: ReactNode, options?: ExternalToast) {
    return sonner.info(withRole('status', message), options)
  },
  message(message: ReactNode, options?: ExternalToast) {
    return sonner(withRole('status', message), options)
  },
  error(message: ReactNode, options?: ExternalToast) {
    return sonner.error(withRole('alert', message), options)
  },
  dismiss(id?: string | number) {
    return sonner.dismiss(id)
  },
}
