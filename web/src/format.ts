import type { AttendanceStatus, Gender, RelationType, Role } from './api/types'

/** Local calendar date as YYYY-MM-DD (the API's DateOnly format). */
export function toDateString(d: Date): string {
  const pad = (n: number) => String(n).padStart(2, '0')
  return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}`
}

export const today = () => toDateString(new Date())

export function addDays(date: string, days: number): string {
  const d = new Date(`${date}T00:00:00`)
  d.setDate(d.getDate() + days)
  return toDateString(d)
}

export function age(birthDate: string | null, on: string = today()): number | null {
  if (!birthDate) return null
  const [by, bm, bd] = birthDate.split('-').map(Number)
  const [y, m, d] = on.split('-').map(Number)
  return y - by - (m < bm || (m === bm && d < bd) ? 1 : 0)
}

/** First visit within the four weeks up to `on`. */
export function isNewcomer(firstVisitDate: string | null, on: string): boolean {
  return !!firstVisitDate && firstVisitDate <= on && firstVisitDate > addDays(on, -28)
}

export const shortDate = (date: string) => date.slice(5).replace('-', '/')
export const formatTime = (time: string | null) => time?.slice(0, 5) ?? ''
export const percent = (value: number | null) => (value === null ? '—' : `${Math.round(value * 100)}%`)

export function formatDateTime(value: string | null): string {
  if (!value) return ''
  return new Date(value).toLocaleString('zh-TW', { month: 'numeric', day: 'numeric', hour: '2-digit', minute: '2-digit' })
}

export const genderLabel: Record<Gender, string> = { Unknown: '未填', Male: '男', Female: '女' }
export const statusLabel: Record<AttendanceStatus, string> = { Present: '出席', Absent: '缺席', Excused: '請假' }
export const relationLabel: Record<RelationType, string> = { Father: '父親', Mother: '母親', Guardian: '監護人' }
export const roleLabel: Record<Role, string> = { Admin: '系統管理員', Coordinator: '主責同工', Leader: '老師' }
