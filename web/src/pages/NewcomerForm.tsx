import { useState, type FormEvent } from 'react'
import { errorMessage } from '../api/client'
import type { NewcomerRequest } from '../api/types'
import { gradeOptions, today } from '../format'

const empty: NewcomerRequest = {
  name: '',
  birthDate: null,
  grade: null,
  guardianPhone: '',
  consent: false,
}

export function NewcomerForm({
  onSubmit,
  onCancel,
}: {
  onSubmit: (req: NewcomerRequest) => Promise<void>
  onCancel: () => void
}) {
  const [form, setForm] = useState(empty)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const set = <K extends keyof NewcomerRequest>(key: K, value: NewcomerRequest[K]) => setForm((f) => ({ ...f, [key]: value }))

  async function submit(e: FormEvent) {
    e.preventDefault()
    setBusy(true)
    setError(null)
    try {
      await onSubmit(form)
      setForm(empty)
      onCancel()
    } catch (err) {
      setError(errorMessage(err, '新增失敗，請再試一次'))
    } finally {
      setBusy(false)
    }
  }

  return (
    <form className="panel stack" onSubmit={submit}>
      <h2>新增新朋友</h2>
      <div className="grid">
        <label>
          姓名 *
          <input value={form.name} onChange={(e) => set('name', e.target.value)} maxLength={50} required />
        </label>
        <label>
          生日
          <input
            type="date"
            value={form.birthDate ?? ''}
            max={today()}
            onChange={(e) => set('birthDate', e.target.value || null)}
          />
        </label>
        <label>
          年級
          <select
            value={form.grade ?? ''}
            onChange={(e) => set('grade', e.target.value === '' ? null : Number(e.target.value))}
          >
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
            value={form.guardianPhone}
            onChange={(e) => set('guardianPhone', e.target.value)}
            maxLength={20}
          />
        </label>
      </div>
      <label className="inline">
        <input type="checkbox" checked={form.consent} onChange={(e) => set('consent', e.target.checked)} required />
        家長已同意教會為點名與關懷目的保存以上資料
      </label>
      {error && <p className="error">{error}</p>}
      <div className="actions">
        <button type="button" className="secondary" onClick={onCancel}>
          取消
        </button>
        <button type="submit" disabled={busy}>
          {busy ? '新增中…' : '新增並標記出席'}
        </button>
      </div>
    </form>
  )
}
