import { useMutation, useQueryClient } from '@tanstack/react-query'
import { useState, type FormEvent } from 'react'
import { api, errorMessage } from '../api/client'
import type { BatchResult, Group } from '../api/types'
import { gradeOptions, today } from '../format'

type Action = 'move' | 'promote' | 'grade'

/** Toolbar shown while members are ticked in the people list. */
export function BatchBar({
  personIds,
  groups,
  onDone,
}: {
  personIds: number[]
  groups: Group[]
  /** Called after a change was saved (with a summary) or when the selection is cleared (without). */
  onDone: (message?: string) => void
}) {
  const [action, setAction] = useState<Action | null>(null)
  const button = (a: Action, label: string) => (
    <button
      type="button"
      className={`secondary${action === a ? ' active' : ''}`}
      onClick={() => setAction(action === a ? null : a)}
    >
      {label}
    </button>
  )

  return (
    <>
      <div className="batch-bar">
        <strong>已選 {personIds.length} 人</strong>
        {button('move', '移動群組')}
        {button('promote', '升班')}
        {button('grade', '設定年級')}
        <button type="button" className="link" onClick={() => onDone()}>
          取消選取
        </button>
      </div>
      {action === 'move' && <BatchMove personIds={personIds} groups={groups} onDone={onDone} />}
      {action === 'promote' && <BatchPromote personIds={personIds} onDone={onDone} />}
      {action === 'grade' && <BatchGrade personIds={personIds} onDone={onDone} />}
    </>
  )
}

function useRefresh() {
  const queryClient = useQueryClient()
  return () => {
    queryClient.invalidateQueries({ queryKey: ['people'] })
    queryClient.invalidateQueries({ queryKey: ['person'] })
    queryClient.invalidateQueries({ queryKey: ['groups'] })
  }
}

function BatchMove({ personIds, groups, onDone }: { personIds: number[]; groups: Group[]; onDone: (m: string) => void }) {
  const refresh = useRefresh()
  const [groupId, setGroupId] = useState('')
  const [startDate, setStartDate] = useState(today)
  const move = useMutation({
    mutationFn: () =>
      api<BatchResult>('/people/batch/move', {
        method: 'POST',
        body: JSON.stringify({ personIds, groupId: Number(groupId), startDate }),
      }),
    onSuccess: (r) => {
      refresh()
      const name = groups.find((g) => g.id === Number(groupId))?.name
      onDone(`已將 ${r.updated} 人移到「${name}」` + (r.skipped ? `，${r.skipped} 人原本就在該群組` : ''))
    },
  })

  function submit(e: FormEvent) {
    e.preventDefault()
    move.mutate()
  }

  return (
    <form className="panel stack" onSubmit={submit}>
      <h2>移動群組</h2>
      <p className="muted small">勾選的成員會離開同一事工的原群組，從生效日起改到新群組；生效日前的出席紀錄不受影響。</p>
      <div className="toolbar">
        <select value={groupId} onChange={(e) => setGroupId(e.target.value)} required aria-label="移到群組">
          <option value="">移到群組…</option>
          {groups.map((g) => (
            <option key={g.id} value={g.id}>
              {g.ministryName}・{g.name}
            </option>
          ))}
        </select>
        <label className="inline">
          生效日
          <input type="date" value={startDate} onChange={(e) => setStartDate(e.target.value)} required />
        </label>
        <button type="submit" disabled={move.isPending}>
          {move.isPending ? '移動中…' : `移動 ${personIds.length} 人`}
        </button>
      </div>
      {move.isError && <p className="error">{errorMessage(move.error)}</p>}
    </form>
  )
}

function BatchPromote({ personIds, onDone }: { personIds: number[]; onDone: (m: string) => void }) {
  const refresh = useRefresh()
  const promote = useMutation({
    mutationFn: (years: number) =>
      api<BatchResult>('/people/batch/promote', { method: 'POST', body: JSON.stringify({ personIds, years }) }),
    onSuccess: (r, years) => {
      refresh()
      onDone(
        `已將 ${r.updated} 人${years > 0 ? '升' : '降'}一級` +
          (r.skipped ? `，${r.skipped} 人略過（未填年級或已到${years > 0 ? '最高' : '最低'}年級）` : ''),
      )
    },
  })

  return (
    <section className="panel stack">
      <h2>升班</h2>
      <p className="muted small">
        將勾選成員的年級升一級（例如 小班 → 中班、大班 → 小一、小二 → 小三），所在群組不變。未填年級的成員會略過。
      </p>
      <div className="actions">
        <button type="button" className="secondary" disabled={promote.isPending} onClick={() => promote.mutate(-1)}>
          降一級（復原）
        </button>
        <button type="button" disabled={promote.isPending} onClick={() => promote.mutate(1)}>
          {promote.isPending ? '處理中…' : `升一級（${personIds.length} 人）`}
        </button>
      </div>
      {promote.isError && <p className="error">{errorMessage(promote.error)}</p>}
    </section>
  )
}

function BatchGrade({ personIds, onDone }: { personIds: number[]; onDone: (m: string) => void }) {
  const refresh = useRefresh()
  const [grade, setGrade] = useState('')
  const save = useMutation({
    mutationFn: () =>
      api<BatchResult>('/people/batch/grade', {
        method: 'POST',
        body: JSON.stringify({ personIds, grade: grade === 'none' ? null : Number(grade) }),
      }),
    onSuccess: (r) => {
      refresh()
      onDone(`已更新 ${r.updated} 人的年級` + (r.skipped ? `，${r.skipped} 人原本就是此年級` : ''))
    },
  })

  function submit(e: FormEvent) {
    e.preventDefault()
    save.mutate()
  }

  return (
    <form className="panel stack" onSubmit={submit}>
      <h2>設定年級</h2>
      <p className="muted small">設定的是目前的年級，之後每年 8 月會自動升一級。</p>
      <div className="toolbar">
        <select value={grade} onChange={(e) => setGrade(e.target.value)} required aria-label="年級">
          <option value="">選擇年級…</option>
          {gradeOptions.map((g) => (
            <option key={g.value} value={g.value}>
              {g.label}
            </option>
          ))}
          <option value="none">清除（未填）</option>
        </select>
        <button type="submit" disabled={save.isPending}>
          {save.isPending ? '儲存中…' : `套用到 ${personIds.length} 人`}
        </button>
      </div>
      {save.isError && <p className="error">{errorMessage(save.error)}</p>}
    </form>
  )
}
