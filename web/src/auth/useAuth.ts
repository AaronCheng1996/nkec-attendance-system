import { createContext, useContext } from 'react'
import type { CurrentUser, LoginResponse, Role } from '../api/types'

export interface AuthState {
  user: CurrentUser | null
  login: (userName: string, password: string) => Promise<void>
  setSession: (session: LoginResponse) => void
  logout: () => void
  hasRole: (...roles: Role[]) => boolean
}

// Kept apart from AuthProvider so that file exports only a component (React Fast Refresh).
export const AuthContext = createContext<AuthState | null>(null)

export function useAuth() {
  const ctx = useContext(AuthContext)
  if (!ctx) throw new Error('useAuth must be used inside AuthProvider')
  return ctx
}
