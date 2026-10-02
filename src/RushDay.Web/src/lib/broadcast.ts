/**
 * Cross-tab session messages (05-frontend.md section 5.1 step 3): signing out in one tab posts
 * `{ type: 'logout' }` on `BroadcastChannel('rushday.auth')`, and every other open tab clears its
 * cache and returns to the sign-in page too. Where BroadcastChannel is missing (old browsers, some
 * test environments) this degrades to a no-op; the next request in the other tab answers 401 anyway.
 */

export const AUTH_CHANNEL_NAME = 'rushday.auth'

export type AuthMessage = { type: 'logout' }

export interface AuthChannel {
  post(message: AuthMessage): void
  close(): void
}

function isAuthMessage(value: unknown): value is AuthMessage {
  return (
    typeof value === 'object' && value !== null && (value as { type?: unknown }).type === 'logout'
  )
}

export function openAuthChannel(onMessage: (message: AuthMessage) => void): AuthChannel {
  if (typeof BroadcastChannel === 'undefined') {
    return { post: () => {}, close: () => {} }
  }

  let channel: BroadcastChannel
  try {
    channel = new BroadcastChannel(AUTH_CHANNEL_NAME)
  } catch {
    return { post: () => {}, close: () => {} }
  }

  channel.onmessage = (event: MessageEvent<unknown>) => {
    if (isAuthMessage(event.data)) onMessage(event.data)
  }

  return {
    post(message) {
      try {
        channel.postMessage(message)
      } catch {
        // A closed channel cannot post; the other tabs will find out on their next request.
      }
    },
    close() {
      channel.onmessage = null
      channel.close()
    },
  }
}
