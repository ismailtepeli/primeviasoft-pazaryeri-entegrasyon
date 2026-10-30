const TOKEN_KEY = 'pz.token'

export const tokenStore = {
  get: () => localStorage.getItem(TOKEN_KEY),
  set: (token: string) => localStorage.setItem(TOKEN_KEY, token),
  clear: () => localStorage.removeItem(TOKEN_KEY),
}

export class ApiError extends Error {
  status: number
  constructor(status: number, message: string) {
    super(message)
    this.status = status
  }
}

/** Oturum süresi dolduğunda (401) çağrılır; AuthProvider kullanıcıyı giriş ekranına yollar. */
let onUnauthorized: () => void = () => {}
export const setUnauthorizedHandler = (fn: () => void) => { onUnauthorized = fn }

export async function api<T>(path: string, init: RequestInit = {}): Promise<T> {
  const token = tokenStore.get()
  const res = await fetch(`/api${path}`, {
    ...init,
    headers: {
      // FormData'da tarayıcı sınır (boundary) içeren Content-Type'ı kendisi koyar.
      ...(init.body instanceof FormData ? {} : { 'Content-Type': 'application/json' }),
      ...(token ? { Authorization: `Bearer ${token}` } : {}),
      ...init.headers,
    },
  })
  if (res.status === 401 && token) onUnauthorized()
  if (!res.ok) {
    const body = await res.json().catch(() => null)
    throw new ApiError(res.status, body?.error ?? `İstek başarısız (${res.status})`)
  }
  if (res.status === 204) return undefined as T
  return res.json() as Promise<T>
}

/** Dosya indirir (Excel şablonu gibi); yetki başlığı gerektiği için düz bağlantı yerine fetch kullanılır. */
export async function download(path: string, fileName: string): Promise<void> {
  const token = tokenStore.get()
  const res = await fetch(`/api${path}`, { headers: token ? { Authorization: `Bearer ${token}` } : {} })
  if (res.status === 401 && token) onUnauthorized()
  if (!res.ok) throw new ApiError(res.status, `İndirilemedi (${res.status})`)
  saveBlob(await res.blob(), fileName)
}

export function saveBlob(blob: Blob, fileName: string): void {
  const url = URL.createObjectURL(blob)
  const a = document.createElement('a')
  a.href = url
  a.download = fileName
  a.click()
  URL.revokeObjectURL(url)
}
