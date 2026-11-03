import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { BrowserRouter, Link, Route, Routes } from 'react-router'
import { ApiError } from './api/client'
import { AuthProvider } from './auth/AuthContext'
import { useAuth } from './auth/useAuth'
import { RequireAuth } from './auth/RequireAuth'
import { AccountPage } from './pages/AccountPage'
import { HomePage } from './pages/HomePage'
import { LoginPage } from './pages/LoginPage'
import { PeoplePage } from './pages/PeoplePage'
import { PersonPage } from './pages/PersonPage'
import { Placeholder } from './pages/Placeholder'
import { ReportsPage } from './pages/ReportsPage'
import { RollcallPage } from './pages/RollcallPage'
import { UsersPage } from './pages/UsersPage'

const queryClient = new QueryClient({
  defaultOptions: {
    queries: {
      // Client errors (401/403/404/400) will not succeed on retry.
      retry: (count, err) => !(err instanceof ApiError && err.status < 500) && count < 2,
      refetchOnWindowFocus: false,
    },
  },
})

function Header() {
  const { user, logout } = useAuth()
  if (!user) return null
  return (
    <header className="topbar">
      <Link to="/" className="brand">
        點名系統
      </Link>
      <nav>
        <Link to="/account">{user.displayName}</Link>
        <button
          className="link"
          onClick={() => {
            logout()
            queryClient.clear()
          }}
        >
          登出
        </button>
      </nav>
    </header>
  )
}

const managers = ['Admin', 'Coordinator'] as const

export default function App() {
  return (
    <QueryClientProvider client={queryClient}>
      <AuthProvider>
        <BrowserRouter>
          <Header />
          <Routes>
            <Route path="/login" element={<LoginPage />} />
            <Route path="/" element={<RequireAuth><HomePage /></RequireAuth>} />
            <Route path="/rollcall" element={<RequireAuth><RollcallPage /></RequireAuth>} />
            <Route path="/account" element={<RequireAuth><AccountPage /></RequireAuth>} />
            <Route path="/admin/people" element={<RequireAuth roles={[...managers]}><PeoplePage /></RequireAuth>} />
            <Route path="/admin/people/:id" element={<RequireAuth roles={[...managers]}><PersonPage /></RequireAuth>} />
            <Route path="/admin/reports" element={<RequireAuth roles={[...managers]}><ReportsPage /></RequireAuth>} />
            <Route path="/admin/users" element={<RequireAuth roles={['Admin']}><UsersPage /></RequireAuth>} />
            <Route path="*" element={<Placeholder title="找不到頁面" />} />
          </Routes>
        </BrowserRouter>
      </AuthProvider>
    </QueryClientProvider>
  )
}
