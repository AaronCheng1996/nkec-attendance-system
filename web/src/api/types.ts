export type Role = 'Admin' | 'Coordinator' | 'Leader'
export type Gender = 'Unknown' | 'Male' | 'Female'
export type AttendanceStatus = 'Present' | 'Absent' | 'Excused'
export type RelationType = 'Father' | 'Mother' | 'Guardian'

export interface CurrentUser {
  id: string
  userName: string
  displayName: string
  roles: Role[]
}

export interface LoginResponse {
  accessToken: string
  user: CurrentUser
}

export interface Group {
  id: number
  name: string
  ministryId: number
  ministryName: string
  defaultDayOfWeek: string | null
  defaultStartTime: string | null
  memberCount: number
}

export interface Ministry {
  id: number
  name: string
  groups: { id: number; name: string }[]
}

export interface RosterPerson {
  personId: number
  name: string
  nickname: string | null
  gender: Gender
  birthDate: string | null
  grade: number | null
  firstVisitDate: string | null
  isVisitor: boolean
  status: AttendanceStatus | null
}

export interface Rollcall {
  groupId: number
  groupName: string
  date: string
  sessionId: number | null
  isCancelled: boolean
  lastMarkedAt: string | null
  lastMarkedBy: string | null
  people: RosterPerson[]
}

export interface NewcomerRequest {
  name: string
  birthDate: string | null
  grade: number | null
  /** 聯絡電話。沒有填家長姓名時存在孩子本人的資料上，關懷名單會顯示。 */
  guardianPhone: string
  consent: boolean
}

export interface PersonListItem {
  id: number
  name: string
  nickname: string | null
  gender: Gender
  birthDate: string | null
  grade: number | null
  firstVisitDate: string | null
  isActive: boolean
  groups: string[]
}

export interface PersonDetail {
  id: number
  name: string
  nickname: string | null
  gender: Gender
  birthDate: string | null
  grade: number | null
  phone: string | null
  email: string | null
  firstVisitDate: string | null
  consentAt: string | null
  notes: string | null
  isActive: boolean
  guardians: { personId: number; name: string; phone: string | null; relationType: RelationType; isPrimaryContact: boolean }[]
  memberships: { id: number; groupId: number; groupName: string; startDate: string; endDate: string | null }[]
  recentAttendance: { date: string; groupName: string; status: AttendanceStatus }[]
}

export interface UserDto {
  id: string
  userName: string
  displayName: string
  isActive: boolean
  isLockedOut: boolean
  role: Role | null
  groupIds: number[]
  ministryIds: number[]
  createdAt: string
}

export interface SessionStat {
  sessionId: number
  date: string
  groupId: number
  groupName: string
  present: number
  absent: number
  excused: number
  rate: number | null
}

export interface ReportSummary {
  sessions: number
  avgPresent: number
  avgRate: number | null
  distinctAttendees: number
  newcomers: number
  sessionStats: SessionStat[]
}

export interface Absentee {
  personId: number
  name: string
  groupName: string
  consecutiveAbsences: number
  lastPresentDate: string | null
  contactName: string | null
  contactPhone: string | null
}

export interface NewcomerStat {
  personId: number
  name: string
  firstVisitDate: string
  groupName: string | null
  timesPresent: number
  sessionsSince: number
  lastPresentDate: string | null
}
