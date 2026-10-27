import { Link } from 'react-router'
import { useAuth } from '../auth/AuthContext'

export function HomePage() {
  const { user, hasRole } = useAuth()

  return (
    <main className="page">
      <h1>平安，{user?.displayName}</h1>
      <nav className="stack">
        <Link className="card primary" to="/rollcall">
          點名
          <small>標記出席、新增新朋友</small>
        </Link>
        {hasRole('Admin', 'Coordinator') && (
          <>
            <Link className="card" to="/admin/reports">
              統計報表
              <small>出席率、關懷名單、新朋友、匯出 Excel</small>
            </Link>
            <Link className="card" to="/admin/people">
              成員管理
              <small>查詢與編輯成員資料、升班換組</small>
            </Link>
          </>
        )}
        {hasRole('Admin') && (
          <Link className="card" to="/admin/users">
            帳號管理
            <small>老師與同工的帳號、角色與負責群組</small>
          </Link>
        )}
      </nav>
    </main>
  )
}
