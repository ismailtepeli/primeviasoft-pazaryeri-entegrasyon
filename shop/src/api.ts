export class ShopApiError extends Error {
  status: number
  code?: string
  constructor(status: number, message: string, code?: string) {
    super(message)
    this.status = status
    this.code = code
  }
}

/**
 * Mağaza API'si (api/shop). Vitrin ve giriş uçları herkese açıktır ve kiracı slug ile verilir (`slug` sorgu parametresi eklenir);
 * müşteri uçları (`/me/...`) Bearer token ister ve kiracıyı token'dan bilir.
 */
export async function shopApi<T>(slug: string, path: string, init: RequestInit = {}, token?: string | null): Promise<T> {
  const isAccount = path.startsWith('/me')
  const url = `/api/shop${path}${isAccount ? '' : `${path.includes('?') ? '&' : '?'}slug=${encodeURIComponent(slug)}`}`
  const res = await fetch(url, {
    ...init,
    headers: { ...(init.body ? { 'Content-Type': 'application/json' } : {}), ...(token ? { Authorization: `Bearer ${token}` } : {}), ...init.headers },
  })
  if (!res.ok) {
    const body = await res.json().catch(() => null)
    const fallback = res.status === 429 ? 'Çok fazla istek; bir dakika sonra tekrar deneyin.' : `İstek başarısız (${res.status})`
    throw new ShopApiError(res.status, body?.error ?? body?.message ?? fallback, body?.code)
  }
  if (res.status === 204) return undefined as T
  return res.json() as Promise<T>
}
