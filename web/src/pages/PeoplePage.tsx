import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useDeferredValue, useState, type FormEvent } from 'react'
import { Link } from 'react-router'
import { api, errorMessage } from '../api/client'
import type { Gender, Group, PersonListItem } from '../api/types'
import { useAuth } from '../auth/useAuth'
import { age, genderLabel, gradeLabel, gradeOptions, today } from '../format'

export function PeoplePage() {
  const [search, setSearch] = useState('')
  const [groupId, setGroupId] = useState('')
  const [includeInactive, setIncludeInactive] = useState(false)
  const [adding, setAdding] = useState(false)
  const [message, setMessage] = useState<string | null>(null)
  const { hasRole } = useAuth()
  const deferredSearch = useDeferredValue(search.trim())

  const groups = useQuery({ queryKey: ['groups'], queryFn: () => api<Group[]>('/groups') })
  const people = useQuery({
    queryKey: ['people', deferredSearch, groupId, includeInactive],
    queryFn: () => {
      const q = new URLSearchParams()
      if (deferredSearch) q.set('search', deferredSearch)
      if (groupId) q.set('groupId', groupId)
      if (includeInactive) q.set('includeInactive', 'true')
      return api<PersonListItem[]>(`/people?${q}`)
    },
    placeholderData: (prev) => prev,
  })

  return (
    <main className="page">
      <div className="section-head">
        <h1>成員管理</h1>
        <button type="button" onClick={() => setAdding(true)} disabled={!groups.data?.length}>
          ＋新增成員
        </button>
      </div>
      {groups.data?.length === 0 && (
        <p className="muted">
          還沒有任何群組，請先
          {hasRole('Admin') ? <Link to="/admin/groups">建立事工與群組</Link> : '請管理員建立事工與群組'}
          ，才能新增成員。
        </p>
      )}
      {message && <p className="muted">{message}</p>}
      {adding && groups.data && (
        <NewPersonForm
          groups={groups.data}
          defaultGroupId={groupId}
          onDone={(name) => {
            setAdding(false)
            if (name) setMessage(`已新增「${name}」`)
          }}
        />
      )}
      <div className="toolbar">
        <input type="search" placeholder="搜尋姓名" value={search} onChange={(e) => setSearch(e.target.value)} />
        <select value={groupId} onChange={(e) => setGroupId(e.target.value)} aria-label="群組">
          <option value="">全部群組</option>
          {groups.data?.map((g) => (
            <option key={g.id} value={g.id}>
              {g.name}
            </option>
          ))}
        </select>
        <label className="inline">
          <input type="checkbox" checked={includeInactive} onChange={(e) => setIncludeInactive(e.target.checked)} />
          含已停用
        </label>
      </div>

      {people.isError && <p className="error">{errorMessage(people.error)}</p>}
      {people.data && (
        <>
          <p className="muted">共 {people.data.length} 人{people.data.length >= 300 && '（僅顯示前 300 筆，請用搜尋縮小範圍）'}</p>
          <div className="table-wrap">
            <table>
              <thead>
                <tr>
                  <th>姓名</th>
                  <th>性別</th>
                  <th>年齡</th>
                  <th>年級</th>
                  <th>目前群組</th>
                  <th>第一次來</th>
                </tr>
              </thead>
              <tbody>
                {people.data.map((p) => (
                  <tr key={p.id} className={p.isActive ? '' : 'inactive'}>
                    <td>
                      <Link to={`/admin/people/${p.id}`}>{p.name}</Link>
                      {p.nickname && <span className="muted">（{p.nickname}）</span>}
                      {!p.isActive && <span className="badge">停用</span>}
                    </td>
                    <td>{genderLabel[p.gender]}</td>
                    <td>{age(p.birthDate) ?? '—'}</td>
                    <td>{gradeLabel(p.grade) ?? '—'}</td>
                    <td>{p.groups.join('、') || '—'}</td>
                    <td>{p.firstVisitDate ?? '—'}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </>
      )}
    </main>
  )
}

function NewPersonForm({
  groups,
  defaultGroupId,
  onDone,
}: {
  groups: Group[]
  defaultGroupId: string
  onDone: (createdName?: string) => void
}) {
  const queryClient = useQueryClient()
  const [form, setForm] = useState({
    name: '',
    gender: 'Unknown' as Gender,
    birthDate: '',
    grade: '',
    phone: '',
    groupId: defaultGroupId || String(groups[0].id),
    startDate: today(),
    consent: false,
  })
  const [error, setError] = useState<string | null>(null)

  const save = useMutation({
    mutationFn: () =>
      api('/people', {
        method: 'POST',
        body: JSON.stringify({
          ...form,
          birthDate: form.birthDate || null,
          grade: form.grade === '' ? null : Number(form.grade),
          groupId: Number(form.groupId),
        }),
      }),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['people'] })
      queryClient.invalidateQueries({ queryKey: ['groups'] })
      onDone(form.name.trim())
    },
    onError: (err) => setError(errorMessage(err)),
  })

  const set = <K extends keyof typeof form>(key: K, value: (typeof form)[K]) => setForm((f) => ({ ...f, [key]: value }))

  function submit(e: FormEvent) {
    e.preventDefault()
    setError(null)
    save.mutate()
  }

  return (
    <form className="panel stack" onSubmit={submit}>
      <h2>新增成員</h2>
      <p className="muted small">用於登記既有的成員。第一次來的新朋友請在點名頁按「新增新朋友」，才會列入新朋友統計。</p>
      <div className="grid">
        <label>
          姓名 *
          <input value={form.name} onChange={(e) => set('name', e.target.value)} maxLength={50} required />
        </label>
        <label>
          群組 *
          <select value={form.groupId} onChange={(e) => set('groupId', e.target.value)}>
            {groups.map((g) => (
              <option key={g.id} value={g.id}>
                {g.ministryName}・{g.name}
              </option>
            ))}
          </select>
        </label>
        <label>
          性別
          <select value={form.gender} onChange={(e) => set('gender', e.target.value as Gender)}>
            <option value="Unknown">未填</option>
            <option value="Male">男</option>
            <option value="Female">女</option>
          </select>
        </label>
        <label>
          生日
          <input type="date" value={form.birthDate} max={today()} onChange={(e) => set('birthDate', e.target.value)} />
        </label>
        <label>
          年級
          <select value={form.grade} onChange={(e) => set('grade', e.target.value)}>
            <option value="">未填</option>
            {gradeOptions.map((g) => (
              <option key={g.value} value={g.value}>
                {g.label}
              </option>
            ))}
          </select>
        </label>
        <label>
          聯絡電話
          <input
            type="tel"
            inputMode="tel"
            value={form.phone}
            onChange={(e) => set('phone', e.target.value)}
            maxLength={20}
          />
        </label>
        <label>
          加入群組日期
          <input type="date" value={form.startDate} onChange={(e) => set('startDate', e.target.value)} required />
        </label>
      </div>
      <label className="inline">
        <input type="checkbox" checked={form.consent} onChange={(e) => set('consent', e.target.checked)} required />
        家長已同意教會為點名與關懷目的保存以上資料
      </label>
      {error && <p className="error">{error}</p>}
      <div className="actions">
        <button type="button" className="secondary" onClick={() => onDone()}>
          取消
        </button>
        <button type="submit" disabled={save.isPending}>
          {save.isPending ? '新增中…' : '新增'}
        </button>
      </div>
    </form>
  )
}
