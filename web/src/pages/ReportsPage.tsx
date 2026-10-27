import { useQuery } from '@tanstack/react-query'
import { useState } from 'react'
import { Link } from 'react-router'
import { api, download, errorMessage } from '../api/client'
import type { Absentee, Group, NewcomerStat, ReportSummary } from '../api/types'
import { addDays, percent, shortDate, today } from '../format'

export function ReportsPage() {
  const [from, setFrom] = useState(() => addDays(today(), -56))
  const [to, setTo] = useState(today)
  const [groupId, setGroupId] = useState('')
  const [minAbsences, setMinAbsences] = useState(3)
  const [exportError, setExportError] = useState<string | null>(null)

  const range = new URLSearchParams({ from, to, ...(groupId ? { groupId } : {}) }).toString()
  const valid = !!from && !!to && from <= to

  const groups = useQuery({ queryKey: ['groups'], queryFn: () => api<Group[]>('/groups') })
  const summary = useQuery({
    queryKey: ['report-summary', range],
    queryFn: () => api<ReportSummary>(`/reports/summary?${range}`),
    enabled: valid,
  })
  const newcomers = useQuery({
    queryKey: ['report-newcomers', range],
    queryFn: () => api<NewcomerStat[]>(`/reports/newcomers?${range}`),
    enabled: valid,
  })
  const absentees = useQuery({
    queryKey: ['report-absentees', groupId, minAbsences],
    queryFn: () =>
      api<Absentee[]>(`/reports/absentees?minAbsences=${minAbsences}${groupId ? `&groupId=${groupId}` : ''}`),
  })

  async function exportExcel() {
    setExportError(null)
    try {
      await download(`/reports/export?${range}`, 'attendance.xlsx')
    } catch (err) {
      setExportError(errorMessage(err, '匯出失敗'))
    }
  }

  const s = summary.data
  const maxPresent = Math.max(1, ...(s?.sessionStats.map((x) => x.present + x.absent + x.excused) ?? []))

  return (
    <main className="page stack">
      <h1>統計報表</h1>
      <div className="toolbar">
        <label className="inline">
          從 <input type="date" value={from} onChange={(e) => setFrom(e.target.value)} />
        </label>
        <label className="inline">
          到 <input type="date" value={to} onChange={(e) => setTo(e.target.value)} />
        </label>
        <select value={groupId} onChange={(e) => setGroupId(e.target.value)} aria-label="群組">
          <option value="">全部群組</option>
          {groups.data?.map((g) => (
            <option key={g.id} value={g.id}>
              {g.name}
            </option>
          ))}
        </select>
        <button type="button" className="secondary" onClick={exportExcel} disabled={!valid}>
          匯出 Excel
        </button>
      </div>
      {!valid && <p className="error">請選擇正確的日期範圍</p>}
      {exportError && <p className="error">{exportError}</p>}
      {summary.isError && <p className="error">{errorMessage(summary.error)}</p>}

      {s && (
        <>
          <div className="tiles">
            <Tile label="聚會場次" value={s.sessions} />
            <Tile label="平均出席人數" value={s.avgPresent.toFixed(1)} />
            <Tile label="平均出席率" value={percent(s.avgRate)} />
            <Tile label="出席過的人數" value={s.distinctAttendees} />
            <Tile label="新朋友" value={s.newcomers} />
          </div>

          <section className="panel stack">
            <h2>各場次出席</h2>
            {s.sessionStats.length === 0 && <p className="muted">這段期間沒有點名紀錄</p>}
            <div className="table-wrap">
              <table>
                <thead>
                  <tr>
                    <th>日期</th>
                    <th>群組</th>
                    <th className="bar-col">出席 / 缺席 / 請假</th>
                    <th>出席率</th>
                  </tr>
                </thead>
                <tbody>
                  {s.sessionStats.map((x) => (
                    <tr key={x.sessionId}>
                      <td>{shortDate(x.date)}</td>
                      <td>{x.groupName}</td>
                      <td className="bar-col">
                        <div className="bar" title={`出席 ${x.present}、缺席 ${x.absent}、請假 ${x.excused}`}>
                          <span className="present" style={{ width: `${(x.present / maxPresent) * 100}%` }} />
                          <span className="excused" style={{ width: `${(x.excused / maxPresent) * 100}%` }} />
                          <span className="absent" style={{ width: `${(x.absent / maxPresent) * 100}%` }} />
                        </div>
                        <small className="muted">
                          {x.present} / {x.absent} / {x.excused}
                        </small>
                      </td>
                      <td>{percent(x.rate)}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          </section>
        </>
      )}

      <section className="panel stack">
        <div className="section-head">
          <h2>關懷名單</h2>
          <label className="inline">
            連續缺席
            <select value={minAbsences} onChange={(e) => setMinAbsences(Number(e.target.value))}>
              {[2, 3, 4, 6, 8].map((n) => (
                <option key={n} value={n}>
                  {n} 次以上
                </option>
              ))}
            </select>
          </label>
        </div>
        {absentees.isError && <p className="error">{errorMessage(absentees.error)}</p>}
        {absentees.data?.length === 0 && <p className="muted">目前沒有需要關懷的成員</p>}
        {!!absentees.data?.length && (
          <div className="table-wrap">
            <table>
              <thead>
                <tr>
                  <th>姓名</th>
                  <th>群組</th>
                  <th>連續缺席</th>
                  <th>最後出席</th>
                  <th>聯絡人</th>
                </tr>
              </thead>
              <tbody>
                {absentees.data.map((a) => (
                  <tr key={`${a.personId}-${a.groupName}`}>
                    <td>
                      <Link to={`/admin/people/${a.personId}`}>{a.name}</Link>
                    </td>
                    <td>{a.groupName}</td>
                    <td>{a.consecutiveAbsences} 次</td>
                    <td>{a.lastPresentDate ?? '—'}</td>
                    <td>
                      {a.contactName}{' '}
                      {a.contactPhone && <a href={`tel:${a.contactPhone}`}>{a.contactPhone}</a>}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </section>

      <section className="panel stack">
        <h2>新朋友</h2>
        {newcomers.data?.length === 0 && <p className="muted">這段期間沒有新朋友</p>}
        {!!newcomers.data?.length && (
          <div className="table-wrap">
            <table>
              <thead>
                <tr>
                  <th>姓名</th>
                  <th>第一次來</th>
                  <th>群組</th>
                  <th>之後出席</th>
                  <th>最後出席</th>
                </tr>
              </thead>
              <tbody>
                {newcomers.data.map((n) => (
                  <tr key={n.personId}>
                    <td>
                      <Link to={`/admin/people/${n.personId}`}>{n.name}</Link>
                    </td>
                    <td>{n.firstVisitDate}</td>
                    <td>{n.groupName ?? '—'}</td>
                    <td>
                      {n.timesPresent} / {n.sessionsSince} 次
                    </td>
                    <td>{n.lastPresentDate ?? '—'}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </section>
    </main>
  )
}

function Tile({ label, value }: { label: string; value: string | number }) {
  return (
    <div className="tile">
      <span className="muted">{label}</span>
      <strong>{value}</strong>
    </div>
  )
}
