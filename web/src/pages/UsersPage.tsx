import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useState, type FormEvent } from 'react'
import { api, errorMessage } from '../api/client'
import type { Ministry, Role, UserDto } from '../api/types'
import { useAuth } from '../auth/AuthContext'
import { roleLabel } from '../format'

type Editing = { mode: 'create' } | { mode: 'edit'; user: UserDto } | null

export function UsersPage() {
  const users = useQuery({ queryKey: ['users'], queryFn: () => api<UserDto[]>('/users') })
  const ministries = useQuery({ queryKey: ['ministries'], queryFn: () => api<Ministry[]>('/ministries') })
  const [editing, setEditing] = useState<Editing>(null)
  const [message, setMessage] = useState<string | null>(null)

  const groupName = (id: number) =>
    ministries.data?.flatMap((m) => m.groups).find((g) => g.id === id)?.name ?? `#${id}`
  const ministryName = (id: number) => ministries.data?.find((m) => m.id === id)?.name ?? `#${id}`

  async function resetPassword(user: UserDto) {
    const password = prompt(`為「${user.displayName}」設定新密碼（至少 8 碼，需含英文小寫與數字）`)
    if (!password) return
    try {
      await api(`/users/${user.id}/reset-password`, { method: 'POST', body: JSON.stringify({ newPassword: password }) })
      setMessage(`已重設「${user.displayName}」的密碼，請以安全的方式告知本人`)
      users.refetch()
    } catch (err) {
      setMessage(errorMessage(err))
    }
  }

  return (
    <main className="page stack">
      <div className="section-head">
        <h1>帳號管理</h1>
        <button type="button" onClick={() => setEditing({ mode: 'create' })}>
          ＋新增帳號
        </button>
      </div>
      {message && <p className="muted">{message}</p>}

      {editing && (
        <UserForm
          key={editing.mode === 'edit' ? editing.user.id : 'new'}
          editing={editing}
          ministries={ministries.data ?? []}
          onDone={() => setEditing(null)}
        />
      )}

      {users.isError && <p className="error">{errorMessage(users.error)}</p>}
      <div className="table-wrap">
        <table>
          <thead>
            <tr>
              <th>帳號</th>
              <th>名稱</th>
              <th>角色</th>
              <th>負責範圍</th>
              <th>狀態</th>
              <th />
            </tr>
          </thead>
          <tbody>
            {users.data?.map((u) => (
              <tr key={u.id} className={u.isActive ? '' : 'inactive'}>
                <td>{u.userName}</td>
                <td>{u.displayName}</td>
                <td>{u.role ? roleLabel[u.role] : '—'}</td>
                <td>
                  {[...u.ministryIds.map((id) => `${ministryName(id)}（全部）`), ...u.groupIds.map(groupName)].join('、') ||
                    (u.role === 'Admin' ? '全部' : '—')}
                </td>
                <td>
                  {u.isActive ? '啟用' : '停用'}
                  {u.isLockedOut && <span className="badge">鎖定中</span>}
                </td>
                <td className="row-actions">
                  <button type="button" className="link" onClick={() => setEditing({ mode: 'edit', user: u })}>
                    編輯
                  </button>
                  <button type="button" className="link" onClick={() => resetPassword(u)}>
                    重設密碼
                  </button>
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </main>
  )
}

function UserForm({ editing, ministries, onDone }: { editing: NonNullable<Editing>; ministries: Ministry[]; onDone: () => void }) {
  const queryClient = useQueryClient()
  const { user: me } = useAuth()
  const existing = editing.mode === 'edit' ? editing.user : null
  const isSelf = existing?.id === me?.id

  const [form, setForm] = useState({
    userName: existing?.userName ?? '',
    displayName: existing?.displayName ?? '',
    password: '',
    role: (existing?.role ?? 'Leader') as Role,
    isActive: existing?.isActive ?? true,
    groupIds: existing?.groupIds ?? [],
    ministryIds: existing?.ministryIds ?? [],
  })
  const [error, setError] = useState<string | null>(null)

  const save = useMutation({
    mutationFn: () =>
      existing
        ? api(`/users/${existing.id}`, {
            method: 'PUT',
            body: JSON.stringify({
              displayName: form.displayName,
              isActive: form.isActive,
              role: form.role,
              groupIds: form.groupIds,
              ministryIds: form.ministryIds,
            }),
          })
        : api('/users', { method: 'POST', body: JSON.stringify(form) }),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['users'] })
      onDone()
    },
    onError: (err) => setError(errorMessage(err)),
  })

  const toggle = (key: 'groupIds' | 'ministryIds', id: number) =>
    setForm((f) => ({ ...f, [key]: f[key].includes(id) ? f[key].filter((x) => x !== id) : [...f[key], id] }))

  function submit(e: FormEvent) {
    e.preventDefault()
    setError(null)
    save.mutate()
  }

  return (
    <form className="panel stack" onSubmit={submit}>
      <h2>{existing ? `編輯「${existing.displayName}」` : '新增帳號'}</h2>
      <div className="grid">
        <label>
          帳號
          <input
            value={form.userName}
            onChange={(e) => setForm({ ...form, userName: e.target.value })}
            disabled={!!existing}
            autoCapitalize="none"
            required
          />
        </label>
        <label>
          顯示名稱
          <input
            value={form.displayName}
            onChange={(e) => setForm({ ...form, displayName: e.target.value })}
            maxLength={50}
            required
          />
        </label>
        {!existing && (
          <label>
            初始密碼（至少 8 碼，含英文小寫與數字）
            <input
              type="password"
              value={form.password}
              onChange={(e) => setForm({ ...form, password: e.target.value })}
              autoComplete="new-password"
              required
            />
          </label>
        )}
        <label>
          角色
          <select
            value={form.role}
            onChange={(e) => setForm({ ...form, role: e.target.value as Role })}
            disabled={isSelf}
          >
            {(Object.keys(roleLabel) as Role[]).map((r) => (
              <option key={r} value={r}>
                {roleLabel[r]}
              </option>
            ))}
          </select>
        </label>
      </div>

      {form.role === 'Coordinator' && (
        <fieldset>
          <legend>負責的事工（可看該事工所有群組與報表）</legend>
          {ministries.map((m) => (
            <label key={m.id} className="inline">
              <input type="checkbox" checked={form.ministryIds.includes(m.id)} onChange={() => toggle('ministryIds', m.id)} />
              {m.name}
            </label>
          ))}
        </fieldset>
      )}

      {form.role !== 'Admin' && (
        <fieldset>
          <legend>負責點名的群組</legend>
          {ministries.map((m) =>
            m.groups.map((g) => (
              <label key={g.id} className="inline">
                <input type="checkbox" checked={form.groupIds.includes(g.id)} onChange={() => toggle('groupIds', g.id)} />
                {m.name}・{g.name}
              </label>
            )),
          )}
        </fieldset>
      )}

      {existing && (
        <label className="inline">
          <input
            type="checkbox"
            checked={form.isActive}
            onChange={(e) => setForm({ ...form, isActive: e.target.checked })}
            disabled={isSelf}
          />
          啟用（停用後立即無法登入）
        </label>
      )}

      {error && <p className="error">{error}</p>}
      <div className="actions">
        <button type="button" className="secondary" onClick={onDone}>
          取消
        </button>
        <button type="submit" disabled={save.isPending}>
          {save.isPending ? '儲存中…' : '儲存'}
        </button>
      </div>
    </form>
  )
}
