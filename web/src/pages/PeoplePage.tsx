import { useQuery } from '@tanstack/react-query'
import { useDeferredValue, useState } from 'react'
import { Link } from 'react-router'
import { api, errorMessage } from '../api/client'
import type { Group, PersonListItem } from '../api/types'
import { age, genderLabel } from '../format'

export function PeoplePage() {
  const [search, setSearch] = useState('')
  const [groupId, setGroupId] = useState('')
  const [includeInactive, setIncludeInactive] = useState(false)
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
      <h1>成員管理</h1>
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
