import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useDeferredValue, useState, type FormEvent } from 'react'
import { Link } from 'react-router'
import { api, errorMessage } from '../api/client'
import type { Gender, Group, PersonListItem, RelationType } from '../api/types'
import { useAuth } from '../auth/useAuth'
import { age, genderLabel, gradeLabel, gradeOptions, relationLabel, today } from '../format'
import { BatchBar } from './PeopleBatch'
import { PeopleImport } from './PeopleImport'

export function PeoplePage() {
  const [search, setSearch] = useState('')
  const [groupId, setGroupId] = useState('')
  const [includeInactive, setIncludeInactive] = useState(false)
  const [adding, setAdding] = useState(false)
  const [importing, setImporting] = useState(false)
  const [message, setMessage] = useState<string | null>(null)
  const [selected, setSelected] = useState<ReadonlySet<number>>(new Set())
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

  // Only ticked people still in the list count, so a new search or filter never acts on hidden rows.
  const visibleIds = people.data?.map((p) => p.id) ?? []
  const selectedIds = visibleIds.filter((id) => selected.has(id))
  const allSelected = visibleIds.length > 0 && selectedIds.length === visibleIds.length

  const toggle = (id: number) =>
    setSelected((s) => {
      const next = new Set(s)
      if (!next.delete(id)) next.add(id)
      return next
    })
  const toggleAll = () => setSelected(allSelected ? new Set() : new Set(visibleIds))

  return (
    <main className="page">
      <div className="section-head">
        <h1>成員管理</h1>
        <div className="row-actions">
          <button type="button" className="secondary" onClick={() => setImporting(true)} disabled={!groups.data?.length}>
            匯入 Excel
          </button>
          <button type="button" onClick={() => setAdding(true)} disabled={!groups.data?.length}>
            ＋新增成員
          </button>
        </div>
      </div>
      {groups.data?.length === 0 && (
        <p className="muted">
          還沒有任何群組，請先
          {hasRole('Admin') ? <Link to="/admin/groups">建立事工與群組</Link> : '請管理員建立事工與群組'}
          ，才能新增成員。
        </p>
      )}
      {message && <p className="muted">{message}</p>}
      {selectedIds.length > 0 && groups.data && (
        <BatchBar
          personIds={selectedIds}
          groups={groups.data}
          onDone={(result) => {
            setSelected(new Set())
            setMessage(result ?? null)
          }}
        />
      )}
      {importing && (
        <PeopleImport
          onDone={(result) => {
            setImporting(false)
            if (result) setMessage(result)
          }}
        />
      )}
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
        <input type="search" placeholder="搜尋姓名或家長姓名" value={search} onChange={(e) => setSearch(e.target.value)} />
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
                  <th className="select">
                    <input
                      type="checkbox"
                      aria-label="全選"
                      title="全選"
                      checked={allSelected}
                      ref={(el) => {
                        if (el) el.indeterminate = selectedIds.length > 0 && !allSelected
                      }}
                      onChange={toggleAll}
                      disabled={visibleIds.length === 0}
                    />
                  </th>
                  <th>姓名</th>
                  <th>家長</th>
                  <th>性別</th>
                  <th>年齡</th>
                  <th>年級</th>
                  <th>目前群組</th>
                  <th>第一次來</th>
                </tr>
              </thead>
              <tbody>
                {people.data.map((p) => (
                  <tr
                    key={p.id}
                    className={[p.isActive ? '' : 'inactive', selected.has(p.id) ? 'selected' : ''].join(' ').trim()}
                  >
                    <td className="select">
                      <input
                        type="checkbox"
                        aria-label={`選取 ${p.name}`}
                        checked={selected.has(p.id)}
                        onChange={() => toggle(p.id)}
                      />
                    </td>
                    <td>
                      <Link to={`/admin/people/${p.id}`}>{p.name}</Link>
                      {p.nickname && <span className="muted">（{p.nickname}）</span>}
                      {!p.isActive && <span className="badge">停用</span>}
                    </td>
                    <td>{p.guardians.join('、') || '—'}</td>
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
    guardianName: '',
    guardianRelation: 'Mother' as RelationType,
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
          家長姓名
          <input value={form.guardianName} onChange={(e) => set('guardianName', e.target.value)} maxLength={50} />
        </label>
        <label>
          家長關係
          <select
            value={form.guardianRelation}
            onChange={(e) => set('guardianRelation', e.target.value as RelationType)}
            disabled={!form.guardianName.trim()}
          >
            {Object.entries(relationLabel).map(([value, label]) => (
              <option key={value} value={value}>
                {label}
              </option>
            ))}
          </select>
        </label>
        <label>
          {form.guardianName.trim() ? '家長電話' : '聯絡電話'}
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
