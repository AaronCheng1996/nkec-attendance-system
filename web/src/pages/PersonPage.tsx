import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useState, type FormEvent } from 'react'
import { Link, useParams } from 'react-router'
import { api, errorMessage } from '../api/client'
import type { Gender, Group, PersonDetail } from '../api/types'
import { age, gradeOptions, relationLabel, statusLabel, today } from '../format'

export function PersonPage() {
  const id = Number(useParams().id)
  const person = useQuery({ queryKey: ['person', id], queryFn: () => api<PersonDetail>(`/people/${id}`) })

  if (person.isPending) return <main className="page">載入中…</main>
  if (person.isError) return <main className="page error">{errorMessage(person.error)}</main>
  const p = person.data

  return (
    <main className="page stack">
      <p>
        <Link to="/admin/people">← 成員列表</Link>
      </p>
      <h1>
        {p.name} {age(p.birthDate) !== null && <small className="muted">{age(p.birthDate)} 歲</small>}
      </h1>

      <EditPerson key={person.dataUpdatedAt} person={p} />

      <section className="panel stack">
        <h2>家長 / 聯絡人</h2>
        {p.guardians.length === 0 && <p className="muted">尚無資料</p>}
        <ul className="plain">
          {p.guardians.map((g) => (
            <li key={g.personId}>
              {g.name}（{relationLabel[g.relationType]}）{g.phone && <a href={`tel:${g.phone}`}>{g.phone}</a>}
              {g.isPrimaryContact && <span className="badge">主要聯絡人</span>}
            </li>
          ))}
        </ul>
        <p className="muted small">
          {p.consentAt ? `個資同意：${new Date(p.consentAt).toLocaleDateString('zh-TW')}` : '尚未記錄個資同意'}
        </p>
      </section>

      <Memberships person={p} />

      <section className="panel stack">
        <h2>最近出席</h2>
        {p.recentAttendance.length === 0 && <p className="muted">尚無紀錄</p>}
        <ul className="attendance-strip">
          {p.recentAttendance.map((a) => (
            <li key={`${a.date}-${a.groupName}`} className={a.status.toLowerCase()} title={a.groupName}>
              <span>{a.date.slice(5)}</span>
              <strong>{statusLabel[a.status]}</strong>
            </li>
          ))}
        </ul>
      </section>
    </main>
  )
}

function EditPerson({ person }: { person: PersonDetail }) {
  const queryClient = useQueryClient()
  const [form, setForm] = useState({
    name: person.name,
    nickname: person.nickname ?? '',
    gender: person.gender,
    birthDate: person.birthDate ?? '',
    grade: person.grade === null ? '' : String(person.grade),
    phone: person.phone ?? '',
    email: person.email ?? '',
    notes: person.notes ?? '',
    isActive: person.isActive,
  })
  const [message, setMessage] = useState<string | null>(null)

  const save = useMutation({
    mutationFn: () =>
      api(`/people/${person.id}`, {
        method: 'PUT',
        body: JSON.stringify({
          ...form,
          birthDate: form.birthDate || null,
          grade: form.grade === '' ? null : Number(form.grade),
        }),
      }),
    onSuccess: () => {
      setMessage('已儲存')
      queryClient.invalidateQueries({ queryKey: ['person', person.id] })
      queryClient.invalidateQueries({ queryKey: ['people'] })
    },
    onError: (err) => setMessage(errorMessage(err)),
  })

  const set = (key: keyof typeof form, value: string | boolean) => {
    setMessage(null)
    setForm((f) => ({ ...f, [key]: value }))
  }

  function submit(e: FormEvent) {
    e.preventDefault()
    save.mutate()
  }

  return (
    <form className="panel stack" onSubmit={submit}>
      <h2>基本資料</h2>
      <div className="grid">
        <label>
          姓名
          <input value={form.name} onChange={(e) => set('name', e.target.value)} maxLength={50} required />
        </label>
        <label>
          小名 / 英文名
          <input value={form.nickname} onChange={(e) => set('nickname', e.target.value)} maxLength={50} />
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
          電話
          <input type="tel" value={form.phone} onChange={(e) => set('phone', e.target.value)} maxLength={20} />
        </label>
        <label>
          Email
          <input type="email" value={form.email} onChange={(e) => set('email', e.target.value)} maxLength={100} />
        </label>
      </div>
      <label>
        備註
        <textarea value={form.notes} onChange={(e) => set('notes', e.target.value)} maxLength={500} rows={2} />
      </label>
      <label className="inline">
        <input type="checkbox" checked={form.isActive} onChange={(e) => set('isActive', e.target.checked)} />
        啟用（取消勾選後不會出現在點名名單）
      </label>
      <div className="actions">
        {message && <span className={save.isError ? 'error' : 'muted'}>{message}</span>}
        <button type="submit" disabled={save.isPending}>
          儲存
        </button>
      </div>
    </form>
  )
}

function Memberships({ person }: { person: PersonDetail }) {
  const queryClient = useQueryClient()
  const groups = useQuery({ queryKey: ['groups'], queryFn: () => api<Group[]>('/groups') })
  const [targetGroup, setTargetGroup] = useState('')
  const [startDate, setStartDate] = useState(today)
  const [error, setError] = useState<string | null>(null)

  const refresh = () => {
    setError(null)
    queryClient.invalidateQueries({ queryKey: ['person', person.id] })
    queryClient.invalidateQueries({ queryKey: ['people'] })
    queryClient.invalidateQueries({ queryKey: ['groups'] })
  }

  const move = useMutation({
    mutationFn: () =>
      api(`/people/${person.id}/move`, {
        method: 'POST',
        body: JSON.stringify({ groupId: Number(targetGroup), startDate }),
      }),
    onSuccess: refresh,
    onError: (err) => setError(errorMessage(err)),
  })

  const end = useMutation({
    mutationFn: (membershipId: number) => api(`/people/${person.id}/memberships/${membershipId}`, { method: 'DELETE' }),
    onSuccess: refresh,
    onError: (err) => setError(errorMessage(err)),
  })

  const isCurrent = (endDate: string | null) => !endDate || endDate >= today()

  return (
    <section className="panel stack">
      <h2>群組</h2>
      <ul className="plain">
        {person.memberships.map((m) => (
          <li key={m.id}>
            {m.groupName}
            <span className="muted">
              {' '}
              {m.startDate} ～ {m.endDate ?? '迄今'}
            </span>
            {isCurrent(m.endDate) && (
              <button
                type="button"
                className="link"
                disabled={end.isPending}
                onClick={() => confirm(`確定讓 ${person.name} 離開「${m.groupName}」？`) && end.mutate(m.id)}
              >
                離開群組
              </button>
            )}
          </li>
        ))}
      </ul>
      <form
        className="toolbar"
        onSubmit={(e) => {
          e.preventDefault()
          move.mutate()
        }}
      >
        <select value={targetGroup} onChange={(e) => setTargetGroup(e.target.value)} required aria-label="移到群組">
          <option value="">移到群組（升班 / 換組）…</option>
          {groups.data?.map((g) => (
            <option key={g.id} value={g.id}>
              {g.ministryName}・{g.name}
            </option>
          ))}
        </select>
        <input type="date" value={startDate} onChange={(e) => setStartDate(e.target.value)} required aria-label="生效日" />
        <button type="submit" disabled={move.isPending}>
          移動
        </button>
      </form>
      {error && <p className="error">{error}</p>}
    </section>
  )
}
