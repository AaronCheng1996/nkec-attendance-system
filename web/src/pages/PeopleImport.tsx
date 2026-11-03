import { useQueryClient } from '@tanstack/react-query'
import { useState, type FormEvent } from 'react'
import { api, download, errorMessage } from '../api/client'
import type { ImportResult, ImportRowStatus } from '../api/types'
import { genderLabel, gradeLabel, today } from '../format'

const statusLabel: Record<ImportRowStatus, string> = { New: '新增', Duplicate: '已存在，略過', Error: '錯誤' }

/** Upload an Excel list: preview every row first, then import only the new ones. */
export function PeopleImport({ onDone }: { onDone: (message?: string) => void }) {
  const queryClient = useQueryClient()
  const [file, setFile] = useState<File | null>(null)
  const [startDate, setStartDate] = useState(today)
  const [consent, setConsent] = useState(false)
  const [preview, setPreview] = useState<ImportResult | null>(null)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)

  async function send(commit: boolean): Promise<ImportResult> {
    const form = new FormData()
    form.append('file', file!)
    form.append('startDate', startDate)
    form.append('consent', String(consent))
    form.append('commit', String(commit))
    return api<ImportResult>('/people/import', { method: 'POST', body: form })
  }

  async function run(commit: boolean) {
    setBusy(true)
    setError(null)
    try {
      const result = await send(commit)
      if (commit) {
        queryClient.invalidateQueries({ queryKey: ['people'] })
        queryClient.invalidateQueries({ queryKey: ['groups'] })
        onDone(`已匯入 ${result.newCount} 位成員` + (result.duplicateCount ? `，略過 ${result.duplicateCount} 位已存在的成員` : ''))
      } else {
        setPreview(result)
      }
    } catch (err) {
      setError(errorMessage(err, '匯入失敗，請再試一次'))
    } finally {
      setBusy(false)
    }
  }

  function submit(e: FormEvent) {
    e.preventDefault()
    run(false)
  }

  async function downloadTemplate() {
    try {
      await download('/people/import/template', '成員匯入範本.xlsx')
    } catch (err) {
      setError(errorMessage(err))
    }
  }

  return (
    <section className="panel stack">
      <h2>從 Excel 匯入成員</h2>
      <p className="muted small">
        先
        <button type="button" className="link" onClick={downloadTemplate}>
          下載匯入範本
        </button>
        ，在「成員」工作表填好後上傳。上傳後會先列出檢查結果，確認沒問題再匯入；已在系統中的成員會略過，不會覆蓋。
      </p>

      <form className="stack" onSubmit={submit}>
        <div className="grid">
          <label>
            Excel 檔（.xlsx）
            <input
              type="file"
              accept=".xlsx,application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"
              onChange={(e) => {
                setFile(e.target.files?.[0] ?? null)
                setPreview(null)
              }}
              required
            />
          </label>
          <label>
            加入群組日期
            <input type="date" value={startDate} onChange={(e) => setStartDate(e.target.value)} required />
          </label>
        </div>
        <div className="actions">
          <button type="button" className="secondary" onClick={() => onDone()}>
            取消
          </button>
          <button type="submit" disabled={!file || busy}>
            {busy && !preview ? '檢查中…' : '上傳並檢查'}
          </button>
        </div>
      </form>

      {error && <p className="error">{error}</p>}

      {preview && (
        <div className="stack">
          <p>
            共 {preview.rows.length} 列：<strong>新增 {preview.newCount}</strong>・已存在 {preview.duplicateCount}・
            <span className={preview.errorCount ? 'error' : ''}>錯誤 {preview.errorCount}</span>
            {preview.errorCount > 0 && '（有錯誤的列不會匯入，可修正 Excel 後重新上傳）'}
          </p>
          <div className="table-wrap">
            <table>
              <thead>
                <tr>
                  <th>列</th>
                  <th>姓名</th>
                  <th>群組</th>
                  <th>性別</th>
                  <th>生日</th>
                  <th>年級</th>
                  <th>電話</th>
                  <th>結果</th>
                </tr>
              </thead>
              <tbody>
                {preview.rows.map((r) => (
                  <tr key={r.row} className={r.status === 'New' ? '' : 'inactive'}>
                    <td>{r.row}</td>
                    <td>{r.name || '—'}</td>
                    <td>{r.groupName ?? '—'}</td>
                    <td>{r.gender === 'Unknown' ? '—' : genderLabel[r.gender]}</td>
                    <td>{r.birthDate ?? '—'}</td>
                    <td>{gradeLabel(r.grade) ?? '—'}</td>
                    <td>{r.phone ?? '—'}</td>
                    <td>
                      <span className={r.status === 'Error' ? 'error' : r.status === 'New' ? '' : 'muted'}>
                        {statusLabel[r.status]}
                      </span>
                      {r.messages.length > 0 && <div className="small">{r.messages.join('；')}</div>}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
          <label className="inline">
            <input type="checkbox" checked={consent} onChange={(e) => setConsent(e.target.checked)} />
            以上成員的家長已同意教會為點名與關懷目的保存資料
          </label>
          <div className="actions">
            <button type="button" disabled={busy || !consent || preview.newCount === 0} onClick={() => run(true)}>
              {busy ? '匯入中…' : `匯入 ${preview.newCount} 位成員`}
            </button>
          </div>
        </div>
      )}
    </section>
  )
}
