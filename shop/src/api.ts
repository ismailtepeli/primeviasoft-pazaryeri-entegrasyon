export class ShopApiError extends Error {
  status: number
  constructor(status: number, message: string) {
    super(message)
    this.status = status
  }
}

/** Mağaza API'si (api/shop): herkese açık, oturumsuz; kiracı her istekte slug ile verilir. */
export async function shopApi<T>(slug: string, path: string): Promise<T> {
  const url = `/api/shop${path}${path.includes('?') ? '&' : '?'}slug=${encodeURIComponent(slug)}`
  const res = await fetch(url)
  if (!res.ok) {
    const body = await res.json().catch(() => null)
    const fallback = res.status === 429 ? 'Çok fazla istek; bir dakika sonra tekrar deneyin.' : `İstek başarısız (${res.status})`
    throw new ShopApiError(res.status, body?.error ?? body?.message ?? fallback)
  }
  return res.json() as Promise<T>
}
