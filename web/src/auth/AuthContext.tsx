import { useEffect, useState, type ReactNode } from 'react'
import { api, getToken, onUnauthorized, setToken } from '../api/client'
import type { CurrentUser, LoginResponse, Role } from '../api/types'
import { AuthContext } from './useAuth'

const USER_KEY = 'rollcall.user'

function loadUser(): CurrentUser | null {
  if (!getToken()) return null
  try {
    const raw = localStorage.getItem(USER_KEY)
    return raw ? (JSON.parse(raw) as CurrentUser) : null
  } catch {
    return null
  }
}

function storeUser(user: CurrentUser | null) {
  try {
    if (user) localStorage.setItem(USER_KEY, JSON.stringify(user))
    else localStorage.removeItem(USER_KEY)
  } catch {
    // ignore
  }
}

export function AuthProvider({ children }: { children: ReactNode }) {
  const [user, setUser] = useState<CurrentUser | null>(loadUser)

  function setSession(session: LoginResponse) {
    setToken(session.accessToken)
    storeUser(session.user)
    setUser(session.user)
  }

  async function login(userName: string, password: string) {
    setSession(
      await api<LoginResponse>('/auth/login', {
        method: 'POST',
        body: JSON.stringify({ userName, password }),
      }),
    )
  }

  function logout() {
    setToken(null)
    storeUser(null)
    setUser(null)
  }

  useEffect(() => {
    onUnauthorized(logout)
    return () => onUnauthorized(null)
  }, [])

  const hasRole = (...roles: Role[]) => !!user && user.roles.some((r) => roles.includes(r))

  return (
    <AuthContext.Provider value={{ user, login, setSession, logout, hasRole }}>{children}</AuthContext.Provider>
  )
}
