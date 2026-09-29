import { useState } from 'react'
import { ChevronDown, ExternalLink, LogOut, UserRound } from 'lucide-react'
import { Link, useNavigate } from 'react-router'

import { usePublicStatus } from '@/api/endpoints/public'
import { describeProblem } from '@/api/problem'
import { useAuth } from '@/app/AuthProvider'
import {
  Badge,
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuLabel,
  DropdownMenuSeparator,
  DropdownMenuTrigger,
} from '@/components/ui'
import { cn } from '@/lib/cn'
import { formatRole } from '@/lib/format'
import { toast } from '@/lib/toast'

function initials(name: string): string {
  const parts = name.trim().split(/\s+/).filter(Boolean)
  const first = parts[0]?.[0] ?? '?'
  const last = parts.length > 1 ? (parts[parts.length - 1]?.[0] ?? '') : ''
  return (first + last).toUpperCase()
}

export interface UserMenuProps {
  /** Hide the Account link while a gate (forced password change, MFA setup) is in force. */
  gated?: boolean
}

/**
 * Who is signed in and the way out (05-frontend.md section 7): display name, role badge, Account,
 * the institution's privacy notice when configured, and Sign out.
 */
export function UserMenu({ gated = false }: UserMenuProps) {
  const { user, logout } = useAuth()
  const { data: status } = usePublicStatus()
  const navigate = useNavigate()
  const [signingOut, setSigningOut] = useState(false)

  if (!user) return null

  async function signOut() {
    setSigningOut(true)
    try {
      await logout()
      void navigate('/login', { replace: true })
    } catch (error) {
      toast.error(`Couldn't sign you out. ${describeProblem(error).message}`)
    } finally {
      setSigningOut(false)
    }
  }

  const privacyUrl = status?.institution.privacyNoticeUrl

  return (
    <DropdownMenu>
      <DropdownMenuTrigger
        className={cn(
          'flex h-10 cursor-pointer items-center gap-2 rounded-full py-1 pr-2 pl-1 text-sm font-medium text-text hover:bg-surface-2',
          'data-[state=open]:bg-surface-2 pointer-coarse:h-11',
        )}
      >
        <span
          aria-hidden="true"
          className="flex size-8 items-center justify-center rounded-full bg-primary-soft text-xs font-semibold text-primary"
        >
          {initials(user.displayName || user.username)}
        </span>
        <span className="max-w-40 truncate max-md:sr-only">{user.displayName}</span>
        <span className="sr-only">, account menu</span>
        <ChevronDown aria-hidden="true" className="size-4 text-muted" />
      </DropdownMenuTrigger>
      <DropdownMenuContent>
        <DropdownMenuLabel>
          <span className="block font-medium text-text">{user.displayName}</span>
          <span className="block font-mono text-xs text-muted">{user.username}</span>
          <Badge variant="info" className="mt-2">
            {formatRole(user.role)}
          </Badge>
        </DropdownMenuLabel>
        <DropdownMenuSeparator />
        {!gated && (
          <DropdownMenuItem asChild>
            <Link to="/account">
              <UserRound aria-hidden="true" />
              Account
            </Link>
          </DropdownMenuItem>
        )}
        {privacyUrl && (
          <DropdownMenuItem asChild>
            <a href={privacyUrl} target="_blank" rel="noopener noreferrer">
              <ExternalLink aria-hidden="true" />
              Privacy notice
              <span className="sr-only"> (opens in a new tab)</span>
            </a>
          </DropdownMenuItem>
        )}
        {(!gated || privacyUrl) && <DropdownMenuSeparator />}
        <DropdownMenuItem
          disabled={signingOut}
          onSelect={(event) => {
            event.preventDefault()
            void signOut()
          }}
        >
          <LogOut aria-hidden="true" />
          {signingOut ? 'Signing out…' : 'Sign out'}
        </DropdownMenuItem>
      </DropdownMenuContent>
    </DropdownMenu>
  )
}
