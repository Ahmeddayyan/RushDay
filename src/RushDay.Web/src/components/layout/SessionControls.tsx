import { useMutation } from '@tanstack/react-query'
import { LogIn, LogOut } from 'lucide-react'

import { Button } from '@/components/ui/Button'
import { ButtonLink } from '@/components/ui/ButtonLink'
import { Spinner } from '@/components/ui/Spinner'
import { useAuth } from '@/features/auth'

/** Header slot: who is signed in, and the way in or out. */
export function SessionControls() {
  const { status, user, signOut } = useAuth()
  const logout = useMutation({ mutationFn: signOut })

  if (status === 'loading') return <Spinner size="sm" label="Checking your session" />

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
      <Button variant="ghost" size="sm" onClick={() => logout.mutate()} loading={logout.isPending}>
        <LogOut aria-hidden="true" className="size-4" />
        Sign out
      </Button>
    </div>
  )
}
