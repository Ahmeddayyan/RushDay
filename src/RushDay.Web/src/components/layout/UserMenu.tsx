import { useMutation } from '@tanstack/react-query'
import { LogIn, LogOut } from 'lucide-react'

import { useAuth } from '@/app/AuthProvider'
import { Button, ButtonLink, Spinner } from '@/components/ui'

/** Header slot: who is signed in, and the way in or out. Full menu (Radix DropdownMenu) is stage S5. */
export function UserMenu() {
  const { status, user, logout } = useAuth()
  const signOut = useMutation({ mutationFn: logout })

  if (status === 'booting') return <Spinner size="sm" label="Checking your session" />

  if (!user) {
    return (
      <ButtonLink to="/login" variant="secondary" size="sm">
        <LogIn aria-hidden="true" className="size-4" />
        Sign in
      </ButtonLink>
    )
  }

  return (
    <div className="flex items-center gap-2">
      <span className="hidden max-w-48 truncate text-sm text-muted sm:inline" title={user.username}>
        {user.displayName}
      </span>
      <Button
        variant="ghost"
        size="sm"
        onClick={() => signOut.mutate()}
        loading={signOut.isPending}
      >
        <LogOut aria-hidden="true" className="size-4" />
        Sign out
      </Button>
    </div>
  )
}
