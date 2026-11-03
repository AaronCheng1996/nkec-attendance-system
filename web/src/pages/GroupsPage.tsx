import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useState, type FormEvent } from 'react'
import { Link } from 'react-router'
import { api, errorMessage } from '../api/client'
import type { DayOfWeek, GroupDetail, MinistryDetail } from '../api/types'
import { dayLabel, formatTime, gradeLabel, gradeOptions } from '../format'

type Editing =
  | { kind: 'ministry'; ministry?: MinistryDetail }
  | { kind: 'group'; ministryId: number; group?: GroupDetail }
  | null

export function GroupsPage() {
  const org = useQuery({ queryKey: ['organization'], queryFn: () => api<MinistryDetail[]>('/organization') })
  const [editing, setEditing] = useState<Editing>(null)
  const ministries = org.data ?? []

  const formKey =
    editing?.kind === 'ministry' ? `m-${editing.ministry?.id ?? 'new'}` : `g-${editing?.ministryId}-${editing?.group?.id ?? 'new'}`

  return (
    <main className="page stack">
      <div className="section-head">
        <h1>事工與群組</h1>
        <button type="button" onClick={() => setEditing({ kind: 'ministry' })}>
          ＋新增事工
        </button>
      </div>

      {editing?.kind === 'ministry' && (
        <MinistryForm key={formKey} ministry={editing.ministry} onDone={() => setEditing(null)} />
      )}
      {editing?.kind === 'group' && (
        <GroupForm
          key={formKey}
          ministries={ministries}
          ministryId={editing.ministryId}
          group={editing.group}
          onDone={() => setEditing(null)}
        />
      )}

      {org.isPending && <p>載入中…</p>}
      {org.isError && <p className="error">{errorMessage(org.error)}</p>}
      {org.data && ministries.length === 0 && (
        <p className="muted">
          尚未建立任何事工。先新增事工（例如「兒童主日學」），再在事工底下新增群組（例如「第一堂兒童班」），
          最後到<Link to="/admin/people">成員管理</Link>把孩子加進群組。
        </p>
      )}

      {ministries.map((m) => (
        <section key={m.id} className={`panel stack ${m.isActive ? '' : 'inactive'}`}>
          <div className="section-head">
            <h2>
              {m.name} {!m.isActive && <span className="badge">停用</span>}
            </h2>
            <div className="row-actions">
              <button type="button" className="link" onClick={() => setEditing({ kind: 'ministry', ministry: m })}>
                編輯事工
              </button>
              <button type="button" onClick={() => setEditing({ kind: 'group', ministryId: m.id })}>
                ＋新增群組
              </button>
            </div>
          </div>
          {m.groups.length === 0 ? (
            <p className="muted">尚無群組</p>
          ) : (
            <div className="table-wrap">
              <table>
                <thead>
                  <tr>
                    <th>群組</th>
                    <th>聚會時間</th>
                    <th>適用年級</th>
                    <th>人數</th>
                    <th />
                  </tr>
                </thead>
                <tbody>
                  {m.groups.map((g) => (
                    <tr key={g.id} className={g.isActive ? '' : 'inactive'}>
                      <td>
                        {g.name} {!g.isActive && <span className="badge">停用</span>}
                        {g.description && <div className="muted small">{g.description}</div>}
                      </td>
                      <td>
                        {[g.defaultDayOfWeek && dayLabel[g.defaultDayOfWeek], formatTime(g.defaultStartTime)]
                          .filter(Boolean)
                          .join(' ') || '—'}
                      </td>
                      <td>{gradeRange(g)}</td>
                      <td>{g.memberCount}</td>
                      <td className="row-actions">
                        <button
                          type="button"
                          className="link"
                          onClick={() => setEditing({ kind: 'group', ministryId: m.id, group: g })}
                        >
                          編輯
                        </button>
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}
        </section>
      ))}
    </main>
  )
}

function gradeRange(g: GroupDetail): string {
  const from = gradeLabel(g.minGrade)
  const to = gradeLabel(g.maxGrade)
  if (from && to) return from === to ? from : `${from}～${to}`
  return from ? `${from}以上` : to ? `${to}以下` : '—'
}

/** Refreshes every list that shows ministries or groups. */
function useInvalidateOrganization() {
  const queryClient = useQueryClient()
  return () => {
    for (const key of ['organization', 'groups', 'ministries']) queryClient.invalidateQueries({ queryKey: [key] })
  }
}

function MinistryForm({ ministry, onDone }: { ministry?: MinistryDetail; onDone: () => void }) {
  const invalidate = useInvalidateOrganization()
  const [form, setForm] = useState({
    name: ministry?.name ?? '',
    sortOrder: ministry?.sortOrder ?? 0,
    isActive: ministry?.isActive ?? true,
  })
  const [error, setError] = useState<string | null>(null)

  const save = useMutation({
    mutationFn: () =>
      api(ministry ? `/ministries/${ministry.id}` : '/ministries', {
        method: ministry ? 'PUT' : 'POST',
        body: JSON.stringify(form),
      }),
    onSuccess: () => {
      invalidate()
      onDone()
    },
    onError: (err) => setError(errorMessage(err)),
  })

  function submit(e: FormEvent) {
    e.preventDefault()
    setError(null)
    save.mutate()
  }

  return (
    <form className="panel stack" onSubmit={submit}>
      <h2>{ministry ? `編輯「${ministry.name}」` : '新增事工'}</h2>
      <div className="grid">
        <label>
          名稱
          <input
            value={form.name}
            onChange={(e) => setForm({ ...form, name: e.target.value })}
            maxLength={50}
            placeholder="例如：兒童主日學"
            required
          />
        </label>
        <label>
          排序（數字小的在前）
          <input
            type="number"
            value={form.sortOrder}
            onChange={(e) => setForm({ ...form, sortOrder: Number(e.target.value) })}
          />
        </label>
      </div>
      {ministry && (
        <label className="inline">
          <input type="checkbox" checked={form.isActive} onChange={(e) => setForm({ ...form, isActive: e.target.checked })} />
          啟用
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

function GroupForm({
  ministries,
  ministryId,
  group,
  onDone,
}: {
  ministries: MinistryDetail[]
  ministryId: number
  group?: GroupDetail
  onDone: () => void
}) {
  const invalidate = useInvalidateOrganization()
  const [form, setForm] = useState({
    ministryId: group?.ministryId ?? ministryId,
    name: group?.name ?? '',
    description: group?.description ?? '',
    defaultDayOfWeek: group?.defaultDayOfWeek ?? ('Sunday' as DayOfWeek | ''),
    defaultStartTime: group?.defaultStartTime?.slice(0, 5) ?? '',
    sortOrder: group?.sortOrder ?? 0,
    minGrade: group?.minGrade === null || group?.minGrade === undefined ? '' : String(group.minGrade),
    maxGrade: group?.maxGrade === null || group?.maxGrade === undefined ? '' : String(group.maxGrade),
    isActive: group?.isActive ?? true,
  })
  const [error, setError] = useState<string | null>(null)

  const save = useMutation({
    mutationFn: () =>
      api(group ? `/groups/${group.id}` : '/groups', {
        method: group ? 'PUT' : 'POST',
        body: JSON.stringify({
          ...form,
          defaultDayOfWeek: form.defaultDayOfWeek || null,
          defaultStartTime: form.defaultStartTime ? `${form.defaultStartTime}:00` : null,
          minGrade: form.minGrade === '' ? null : Number(form.minGrade),
          maxGrade: form.maxGrade === '' ? null : Number(form.maxGrade),
        }),
      }),
    onSuccess: () => {
      invalidate()
      onDone()
    },
    onError: (err) => setError(errorMessage(err)),
  })

  function submit(e: FormEvent) {
    e.preventDefault()
    setError(null)
    save.mutate()
  }

  const gradeSelect = (key: 'minGrade' | 'maxGrade', label: string) => (
    <label>
      {label}
      <select value={form[key]} onChange={(e) => setForm({ ...form, [key]: e.target.value })}>
        <option value="">不限</option>
        {gradeOptions.map((g) => (
          <option key={g.value} value={g.value}>
            {g.label}
          </option>
        ))}
      </select>
    </label>
  )

  return (
    <form className="panel stack" onSubmit={submit}>
      <h2>{group ? `編輯「${group.name}」` : '新增群組'}</h2>
      <div className="grid">
        <label>
          事工
          <select value={form.ministryId} onChange={(e) => setForm({ ...form, ministryId: Number(e.target.value) })}>
            {ministries.map((m) => (
              <option key={m.id} value={m.id}>
                {m.name}
              </option>
            ))}
          </select>
        </label>
        <label>
          名稱
          <input
            value={form.name}
            onChange={(e) => setForm({ ...form, name: e.target.value })}
            maxLength={50}
            placeholder="例如：第一堂兒童班"
            required
          />
        </label>
        <label>
          聚會星期
          <select
            value={form.defaultDayOfWeek}
            onChange={(e) => setForm({ ...form, defaultDayOfWeek: e.target.value as DayOfWeek | '' })}
          >
            <option value="">不固定</option>
            {(Object.keys(dayLabel) as DayOfWeek[]).map((d) => (
              <option key={d} value={d}>
                {dayLabel[d]}
              </option>
            ))}
          </select>
        </label>
        <label>
          聚會時間
          <input
            type="time"
            value={form.defaultStartTime}
            onChange={(e) => setForm({ ...form, defaultStartTime: e.target.value })}
          />
        </label>
        {gradeSelect('minGrade', '適用年級（從）')}
        {gradeSelect('maxGrade', '適用年級（到）')}
        <label>
          排序（數字小的在前）
          <input
            type="number"
            value={form.sortOrder}
            onChange={(e) => setForm({ ...form, sortOrder: Number(e.target.value) })}
          />
        </label>
        <label>
          說明
          <input
            value={form.description}
            onChange={(e) => setForm({ ...form, description: e.target.value })}
            maxLength={200}
          />
        </label>
      </div>
      {group && (
        <label className="inline">
          <input type="checkbox" checked={form.isActive} onChange={(e) => setForm({ ...form, isActive: e.target.checked })} />
          啟用（停用後不會出現在點名選單）
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
