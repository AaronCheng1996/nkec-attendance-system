const TOKEN_KEY = 'rollcall.accessToken'

export function getToken(): string | null {
  try {
    return localStorage.getItem(TOKEN_KEY)
  } catch {
    return null
  }
}

export function setToken(token: string | null) {
  try {
    if (token) localStorage.setItem(TOKEN_KEY, token)
    else localStorage.removeItem(TOKEN_KEY)
  } catch {
    // storage unavailable (private mode); the session just won't persist
  }
}

export class ApiError extends Error {
  status: number
  constructor(status: number, message: string) {
    super(message)
    this.status = status
  }
}

let unauthorizedHandler: (() => void) | null = null

/** Called when a signed-in request comes back 401 (token expired, password reset, account disabled). */
export function onUnauthorized(handler: (() => void) | null) {
  unauthorizedHandler = handler
}

async function readError(res: Response): Promise<string> {
  const text = await res.text()
  try {
    const problem = JSON.parse(text) as { title?: string; detail?: string; errors?: Record<string, string[]> }
    if (problem.errors) return Object.values(problem.errors).flat().join('；')
    return problem.detail ?? problem.title ?? text
  } catch {
    return text
  }
}

async function request(path: string, init: RequestInit = {}): Promise<Response> {
  const headers = new Headers(init.headers)
  if (init.body && !headers.has('Content-Type')) headers.set('Content-Type', 'application/json')
  const token = getToken()
  if (token) headers.set('Authorization', `Bearer ${token}`)

  const res = await fetch(`/api${path}`, { ...init, headers })
  if (!res.ok) {
    if (res.status === 401 && token) {
      setToken(null)
      unauthorizedHandler?.()
    }
    throw new ApiError(res.status, await readError(res))
  }
  return res
}

export async function api<T>(path: string, init: RequestInit = {}): Promise<T> {
  const res = await request(path, init)
  return res.status === 204 || res.status === 201 ? (undefined as T) : res.json()
}

/** Downloads a file response (e.g. an Excel export) through the authenticated client. */
export async function download(path: string, fallbackName: string) {
  const res = await request(path)
  const blob = await res.blob()
  const name = /filename="?([^";]+)"?/.exec(res.headers.get('Content-Disposition') ?? '')?.[1] ?? fallbackName
  const url = URL.createObjectURL(blob)
  const a = document.createElement('a')
  a.href = url
  a.download = name
  a.click()
  URL.revokeObjectURL(url)
}

export function errorMessage(err: unknown, fallback = '發生錯誤，請稍後再試'): string {
  if (!(err instanceof ApiError)) return '無法連線到伺服器，請檢查網路'
  if (err.status === 403) return '沒有權限執行此操作'
  if (err.status === 429) return '嘗試次數過多，請稍後再試'
  if (err.status >= 500) return fallback
  return err.message || fallback
}
