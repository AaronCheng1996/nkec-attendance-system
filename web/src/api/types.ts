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
  guardianName: string
  guardianRelation: RelationType
  /** 聯絡電話。有填家長姓名時記在家長資料上，否則存在孩子本人的資料上；關懷名單都會顯示。 */
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
  /** 家長姓名，主要聯絡人在前。 */
  guardians: string[]
}

export interface Guardian {
  personId: number
  name: string
  phone: string | null
  relationType: RelationType
  isPrimaryContact: boolean
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
  guardians: Guardian[]
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

export type DayOfWeek = 'Sunday' | 'Monday' | 'Tuesday' | 'Wednesday' | 'Thursday' | 'Friday' | 'Saturday'

export interface GroupDetail {
  id: number
  ministryId: number
  name: string
  description: string | null
  defaultDayOfWeek: DayOfWeek | null
  defaultStartTime: string | null
  sortOrder: number
  minGrade: number | null
  maxGrade: number | null
  isActive: boolean
  memberCount: number
}

export interface MinistryDetail {
  id: number
  name: string
  sortOrder: number
  isActive: boolean
  groups: GroupDetail[]
}

export type ImportRowStatus = 'New' | 'Duplicate' | 'Error'

export interface ImportRow {
  row: number
  name: string
  groupName: string | null
  gender: Gender
  birthDate: string | null
  grade: number | null
  phone: string | null
  guardianName: string | null
  guardianRelation: RelationType | null
  status: ImportRowStatus
  messages: string[]
}

export interface ImportResult {
  committed: boolean
  newCount: number
  duplicateCount: number
  errorCount: number
  rows: ImportRow[]
}

export interface BatchResult {
  updated: number
  skipped: number
}
