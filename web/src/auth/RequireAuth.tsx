import type { ReactNode } from 'react'
import { Navigate, useLocation } from 'react-router'
import type { Role } from '../api/types'
import { useAuth } from './useAuth'

export function RequireAuth({ children, roles }: { children: ReactNode; roles?: Role[] }) {
  const { user, hasRole } = useAuth()
  const location = useLocation()

  if (!user) return <Navigate to="/login" replace state={{ from: location.pathname }} />
  if (roles && !hasRole(...roles)) return <Navigate to="/" replace />
  return children
}
