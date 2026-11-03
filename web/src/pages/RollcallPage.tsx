import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useEffect, useState } from 'react'
import { api, errorMessage } from '../api/client'
import type { AttendanceStatus, Group, NewcomerRequest, Rollcall } from '../api/types'
import { age, formatDateTime, formatTime, genderLabel, gradeLabel, isNewcomer, statusLabel, today } from '../format'
import { NewcomerForm } from './NewcomerForm'

const LAST_GROUP_KEY = 'rollcall.lastGroupId'

function loadLastGroup(): number | null {
  try {
    const value = Number(localStorage.getItem(LAST_GROUP_KEY))
    return Number.isFinite(value) && value > 0 ? value : null
  } catch {
    return null
  }
}

export function RollcallPage() {
  const groups = useQuery({ queryKey: ['groups'], queryFn: () => api<Group[]>('/groups') })
  const [groupId, setGroupId] = useState<number | null>(loadLastGroup)
  const [date, setDate] = useState(today)

  const list = groups.data ?? []
  const group = list.find((g) => g.id === groupId) ?? list[0]

  const rollcall = useQuery({
    queryKey: ['rollcall', group?.id, date],
    queryFn: () => api<Rollcall>(`/groups/${group!.id}/rollcall?date=${date}`),
    enabled: !!group && !!date,
  })

  function selectGroup(id: number) {
    setGroupId(id)
    try {
      localStorage.setItem(LAST_GROUP_KEY, String(id))
    } catch {
      // ignore
    }
  }

  if (groups.isPending) return <main className="page">載入中…</main>
  if (groups.isError) return <main className="page error">{errorMessage(groups.error)}</main>
  if (!group) return <main className="page">您目前沒有負責的群組，請聯絡管理員設定。</main>

  return (
    <main className="page rollcall-page">
      <div className="toolbar">
        <select value={group.id} onChange={(e) => selectGroup(Number(e.target.value))} aria-label="群組">
          {list.map((g) => (
            <option key={g.id} value={g.id}>
              {g.name} {formatTime(g.defaultStartTime)}
            </option>
          ))}
        </select>
        <input type="date" value={date} max={today()} onChange={(e) => setDate(e.target.value)} aria-label="日期" />
      </div>

      {rollcall.isPending && <p>載入名單中…</p>}
      {rollcall.isError && <p className="error">{errorMessage(rollcall.error)}</p>}
      {rollcall.data && (
        <RollcallSheet
          key={`${rollcall.data.groupId}-${rollcall.data.date}-${rollcall.dataUpdatedAt}`}
          data={rollcall.data}
        />
      )}
    </main>
  )
}

function RollcallSheet({ data }: { data: Rollcall }) {
  const queryClient = useQueryClient()
  const [marks, setMarks] = useState<Record<number, AttendanceStatus>>(() =>
    Object.fromEntries(data.people.filter((p) => p.status).map((p) => [p.personId, p.status!])),
  )
  const [search, setSearch] = useState('')
  const [showNewcomer, setShowNewcomer] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const taken = data.sessionId !== null
  const dirty = data.people.some((p) => (marks[p.personId] ?? null) !== p.status)

  useEffect(() => {
    if (!dirty) return
    const warn = (e: BeforeUnloadEvent) => e.preventDefault()
    window.addEventListener('beforeunload', warn)
    return () => window.removeEventListener('beforeunload', warn)
  }, [dirty])

  const queryKey = ['rollcall', data.groupId, data.date]
  const url = `/groups/${data.groupId}/rollcall?date=${data.date}`

  const saveMarks = () =>
    api<Rollcall>(url, {
      method: 'PUT',
      body: JSON.stringify({
        records: data.people.map((p) => ({ personId: p.personId, status: marks[p.personId] ?? 'Absent' })),
      }),
    })

  const save = useMutation({
    mutationFn: saveMarks,
    onSuccess: (result) => queryClient.setQueryData(queryKey, result),
    onError: (err) => setError(errorMessage(err, '儲存失敗，請再試一次')),
  })

  async function addNewcomer(req: NewcomerRequest) {
    // Save pending marks first; the newcomer response reloads the whole roster.
    if (dirty) await saveMarks()
    const result = await api<Rollcall>(`/groups/${data.groupId}/newcomers?date=${data.date}`, {
      method: 'POST',
      body: JSON.stringify(req),
    })
    queryClient.setQueryData(queryKey, result)
  }

  const setStatus = (personId: number, status: AttendanceStatus) => {
    setError(null)
    setMarks((m) => ({ ...m, [personId]: status }))
  }
  const toggle = (personId: number) => setStatus(personId, marks[personId] === 'Present' ? 'Absent' : 'Present')
  const markAllPresent = () =>
    setMarks(Object.fromEntries(data.people.map((p) => [p.personId, marks[p.personId] === 'Excused' ? 'Excused' : 'Present'])))

  const term = search.trim()
  const visible = term
    ? data.people.filter((p) => p.name.includes(term) || p.nickname?.includes(term))
    : data.people
  const present = data.people.filter((p) => marks[p.personId] === 'Present').length
  const excused = data.people.filter((p) => marks[p.personId] === 'Excused').length

  return (
    <>
      <p className="muted">
        {taken
          ? `已點名・最後儲存 ${formatDateTime(data.lastMarkedAt)}（${data.lastMarkedBy}）`
          : '尚未點名。點選姓名標記出席，未標記的人儲存後記為缺席。'}
      </p>

      <div className="toolbar">
        <input type="search" placeholder="搜尋姓名" value={search} onChange={(e) => setSearch(e.target.value)} />
        <button type="button" className="secondary" onClick={markAllPresent}>
          全部出席
        </button>
        <button type="button" className="secondary" onClick={() => setShowNewcomer((v) => !v)}>
          ＋新朋友
        </button>
      </div>

      {showNewcomer && <NewcomerForm onSubmit={addNewcomer} onCancel={() => setShowNewcomer(false)} />}

      <ul className="roster">
        {visible.map((p) => {
          const status = marks[p.personId]
          const years = age(p.birthDate, data.date)
          return (
            <li key={p.personId} className={`roster-row ${status ? status.toLowerCase() : 'unmarked'}`}>
              <button
                type="button"
                className="roster-main"
                onClick={() => toggle(p.personId)}
                aria-pressed={status === 'Present'}
              >
                <span className="check" aria-hidden>
                  {status === 'Present' ? '✓' : ''}
                </span>
                <span className="who">
                  <strong>{p.name}</strong>
                  {p.nickname && <span className="muted">（{p.nickname}）</span>}
                  <small className="muted">
                    {[gradeLabel(p.grade), years !== null ? `${years} 歲` : null, p.gender !== 'Unknown' ? genderLabel[p.gender] : null]
                      .filter(Boolean)
                      .join('・')}
                    {isNewcomer(p.firstVisitDate, data.date) && <span className="badge">新朋友</span>}
                    {p.isVisitor && <span className="badge">非本班</span>}
                  </small>
                </span>
                <span className="state">{status ? statusLabel[status] : '未點'}</span>
              </button>
              <button
                type="button"
                className={`excuse ${status === 'Excused' ? 'on' : ''}`}
                onClick={() => setStatus(p.personId, status === 'Excused' ? 'Absent' : 'Excused')}
                aria-pressed={status === 'Excused'}
              >
                假
              </button>
            </li>
          )
        })}
        {visible.length === 0 && <li className="muted empty">{term ? '找不到符合的姓名' : '此群組目前沒有成員'}</li>}
      </ul>

      <footer className="save-bar">
        <span>
          出席 <strong>{present}</strong> / {data.people.length}
          {excused > 0 && `・請假 ${excused}`}
        </span>
        {error && <span className="error">{error}</span>}
        <button type="button" onClick={() => save.mutate()} disabled={save.isPending || (taken && !dirty)}>
          {save.isPending ? '儲存中…' : taken && !dirty ? '已儲存' : '儲存點名'}
        </button>
      </footer>
    </>
  )
}
