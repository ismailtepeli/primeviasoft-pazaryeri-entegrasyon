export class StoreApiError extends Error {
  status: number
  code?: string
  body?: unknown
  constructor(status: number, message: string, code?: string, body?: unknown) {
    super(message)
    this.status = status
    this.code = code
    this.body = body
  }
}

const tokenKey = (slug: string) => `store.token.${slug}`

export const tokenStore = {
  get: (slug: string) => { try { return localStorage.getItem(tokenKey(slug)) } catch { return null } },
  set: (slug: string, token: string) => { try { localStorage.setItem(tokenKey(slug), token) } catch { /* depolama kapalı olabilir */ } },
  clear: (slug: string) => { try { localStorage.removeItem(tokenKey(slug)) } catch { /* depolama kapalı olabilir */ } },
}

/** Bayi sitesi API'si (api/store); bayi token'ı varsa gönderilir. */
export async function storeApi<T>(path: string, init: RequestInit = {}, token?: string | null): Promise<T> {
  const res = await fetch(`/api/store${path}`, {
    ...init,
    headers: { ...(init.body instanceof FormData ? {} : { 'Content-Type': 'application/json' }), ...(token ? { Authorization: `Bearer ${token}` } : {}), ...init.headers },
  })
  if (!res.ok) {
    const body = await res.json().catch(() => null)
    const fallback = res.status === 429 ? 'Çok fazla istek; bir dakika sonra tekrar deneyin.' : `İstek başarısız (${res.status})`
    throw new StoreApiError(res.status, body?.error ?? body?.message ?? fallback, body?.code, body)
  }
  if (res.status === 204) return undefined as T
  return res.json() as Promise<T>
}
