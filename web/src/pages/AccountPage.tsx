import { useState, type FormEvent } from 'react'
import { api, errorMessage } from '../api/client'
import type { LoginResponse } from '../api/types'
import { useAuth } from '../auth/AuthContext'

export function AccountPage() {
  const { user, setSession } = useAuth()
  const [currentPassword, setCurrentPassword] = useState('')
  const [newPassword, setNewPassword] = useState('')
  const [confirmPassword, setConfirmPassword] = useState('')
  const [message, setMessage] = useState<{ text: string; ok: boolean } | null>(null)
  const [busy, setBusy] = useState(false)

  async function submit(e: FormEvent) {
    e.preventDefault()
    if (newPassword !== confirmPassword) {
      setMessage({ text: '兩次輸入的新密碼不一致', ok: false })
      return
    }
    setBusy(true)
    setMessage(null)
    try {
      // The password change rotates the security stamp; keep the session alive with the new token.
      setSession(
        await api<LoginResponse>('/auth/change-password', {
          method: 'POST',
          body: JSON.stringify({ currentPassword, newPassword }),
        }),
      )
      setCurrentPassword('')
      setNewPassword('')
      setConfirmPassword('')
      setMessage({ text: '密碼已更新', ok: true })
    } catch (err) {
      setMessage({ text: errorMessage(err), ok: false })
    } finally {
      setBusy(false)
    }
  }

  return (
    <main className="page narrow-top">
      <h1>修改密碼</h1>
      <p className="muted">帳號：{user?.userName}</p>
      <form className="stack" onSubmit={submit}>
        <label>
          目前密碼
          <input
            type="password"
            value={currentPassword}
            onChange={(e) => setCurrentPassword(e.target.value)}
            autoComplete="current-password"
            required
          />
        </label>
        <label>
          新密碼（至少 8 碼，含英文小寫與數字）
          <input
            type="password"
            value={newPassword}
            onChange={(e) => setNewPassword(e.target.value)}
            autoComplete="new-password"
            required
          />
        </label>
        <label>
          再輸入一次新密碼
          <input
            type="password"
            value={confirmPassword}
            onChange={(e) => setConfirmPassword(e.target.value)}
            autoComplete="new-password"
            required
          />
        </label>
        {message && <p className={message.ok ? 'muted' : 'error'}>{message.text}</p>}
        <button type="submit" disabled={busy}>
          更新密碼
        </button>
      </form>
    </main>
  )
}
