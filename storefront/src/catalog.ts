export interface StorePrice { amount: number; includesVat: boolean; currency: string; vatRate: number }

export interface StoreStock { quantity: number | null; label: string | null; inStock: boolean }

export interface StoreProduct {
  id: string
  name: string
  code: string
  mainId: string | null
  variant: string | null
  brand: string | null
  categoryPath: string | null
  coverUrl: string | null
  price: StorePrice
  stock: StoreStock
  minOrderQuantity: number
  packSize: number
}

export interface StoreProductPage { items: StoreProduct[]; total: number }

export interface StoreCategory { id: string; name: string; parentId: string | null; productCount: number }

export interface StoreProductDetail {
  id: string
  name: string
  code: string
  mainId: string | null
  variant: string | null
  brand: string | null
  description: string | null
  breadcrumb: { id: string; name: string }[]
  images: string[]
  price: StorePrice
  stock: StoreStock
  minOrderQuantity: number
  packSize: number
  variants: { id: string; variant: string | null; code: string; inStock: boolean }[]
}

export const PAGE_SIZE = 24

export const sortOptions = [
  { value: 'name', label: 'Ada göre (A-Z)' },
  { value: 'name_desc', label: 'Ada göre (Z-A)' },
  { value: 'price', label: 'Fiyat (artan)' },
  { value: 'price_desc', label: 'Fiyat (azalan)' },
] as const

export type ViewMode = 'table' | 'cards'

export interface ListParams { q: string; category: string; sort: string; page: number }

export function parseListParams(search: URLSearchParams): ListParams {
  const sort = search.get('sort') ?? 'name'
  const page = Number(search.get('page'))
  return {
    q: search.get('q')?.trim() ?? '',
    category: search.get('category') ?? '',
    sort: sortOptions.some((o) => o.value === sort) ? sort : 'name',
    page: Number.isInteger(page) && page >= 1 ? page : 1,
  }
}

/** Adres çubuğu için: varsayılan değerler yazılmaz. */
export function toSearchParams(p: ListParams): URLSearchParams {
  const s = new URLSearchParams()
  if (p.q) s.set('q', p.q)
  if (p.category) s.set('category', p.category)
  if (p.sort !== 'name') s.set('sort', p.sort)
  if (p.page > 1) s.set('page', String(p.page))
  return s
}

export function apiQuery(p: ListParams): string {
  const s = toSearchParams(p)
  s.set('page', String(p.page))
  s.set('pageSize', String(PAGE_SIZE))
  s.set('sort', p.sort)
  if (p.category) { s.delete('category'); s.set('categoryId', p.category) }
  return s.toString()
}

const symbols: Record<string, string> = { TRY: '₺', USD: '$', EUR: '€' }

export function formatMoney(amount: number, currency = 'TRY'): string {
  return `${new Intl.NumberFormat('tr-TR', { minimumFractionDigits: 2, maximumFractionDigits: 2 }).format(amount)} ${symbols[currency] ?? currency}`
}

export function vatNote(price: StorePrice): string {
  return `${price.includesVat ? 'KDV dahil' : 'KDV hariç'}`
}

/** Etkin en az sipariş adedi: koli adedinin katına yuvarlanır (sunucu kuralıyla aynı). */
export function effectiveMin(min: number, pack: number): number {
  const p = Math.max(pack, 1)
  return Math.ceil(Math.max(min, 1) / p) * p
}

export interface QuantityCheck { valid: boolean; suggested: number; error: string | null }

export function checkQuantity(text: string, min: number, pack: number): QuantityCheck {
  const p = Math.max(pack, 1)
  const m = effectiveMin(min, p)
  const qty = /^\d+$/.test(text.trim()) ? Number(text.trim()) : NaN
  if (!Number.isFinite(qty) || qty < 1) return { valid: false, suggested: m, error: 'Geçerli bir adet girin.' }
  if (qty < m) return { valid: false, suggested: m, error: `En az ${m} adet sipariş verilebilir.` }
  if (qty % p !== 0) {
    const up = Math.ceil(qty / p) * p
    return { valid: false, suggested: up, error: `${p} adetlik kolilerle satılır; ${up} adet girin.` }
  }
  return { valid: true, suggested: qty, error: null }
}

export function quantityNote(min: number, pack: number): string {
  const m = effectiveMin(min, pack)
  const parts: string[] = []
  if (m > Math.max(pack, 1)) parts.push(`En az ${m} adet`) // tek koli zaten en az adedi içerir
  if (pack > 1) parts.push(`${pack}'li koli`)
  return parts.join(' · ')
}

/** Kategori kimliğinden köke giden yol (kırıntı için), kökten başlayarak. */
export function categoryTrail(categories: StoreCategory[], id: string): StoreCategory[] {
  const trail: StoreCategory[] = []
  for (let cur = categories.find((c) => c.id === id); cur && trail.length < 20; cur = cur.parentId ? categories.find((c) => c.id === cur!.parentId) : undefined) trail.unshift(cur)
  return trail
}

export function childrenOf(categories: StoreCategory[], parentId: string | null): StoreCategory[] {
  return categories.filter((c) => c.parentId === parentId)
}
