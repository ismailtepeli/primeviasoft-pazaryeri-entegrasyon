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

export interface BrandFacet { name: string; count: number }

export interface StoreFacets { brands: BrandFacet[]; priceMin: number | null; priceMax: number | null }

export interface StoreProductPage { items: StoreProduct[]; total: number; facets: StoreFacets | null }

export interface StoreCategory { id: string; name: string; parentId: string | null; productCount: number; imageUrl: string | null; slugPath: string }

export interface StoreProductDetail {
  id: string
  name: string
  code: string
  mainId: string | null
  variant: string | null
  brand: string | null
  description: string | null
  breadcrumb: { id: string; name: string; slugPath: string }[]
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

/** Liste süzgeçleri; kategori adres yolundan (/kategori/giyim/tisort) gelir, bu yüzden burada yoktur. */
export interface ListParams { q: string; sort: string; page: number; brands: string[]; minPrice: string; maxPrice: string; inStock: boolean }

export const emptyParams: ListParams = { q: '', sort: 'name', page: 1, brands: [], minPrice: '', maxPrice: '', inStock: false }

const numberText = (v: string | null): string => (v && /^\d+([.,]\d+)?$/.test(v.trim()) ? v.trim().replace(',', '.') : '')

export function parseListParams(search: URLSearchParams): ListParams {
  const sort = search.get('sort') ?? 'name'
  const page = Number(search.get('page'))
  return {
    q: search.get('q')?.trim() ?? '',
    sort: sortOptions.some((o) => o.value === sort) ? sort : 'name',
    page: Number.isInteger(page) && page >= 1 ? page : 1,
    brands: [...new Set(search.getAll('brand').map((b) => b.trim()).filter(Boolean))],
    minPrice: numberText(search.get('min')),
    maxPrice: numberText(search.get('max')),
    inStock: search.get('stock') === '1',
  }
}

/** Adres çubuğu için: varsayılan değerler yazılmaz. */
export function toSearchParams(p: ListParams): URLSearchParams {
  const s = new URLSearchParams()
  if (p.q) s.set('q', p.q)
  if (p.sort !== 'name') s.set('sort', p.sort)
  for (const b of p.brands) s.append('brand', b)
  if (p.minPrice) s.set('min', p.minPrice)
  if (p.maxPrice) s.set('max', p.maxPrice)
  if (p.inStock) s.set('stock', '1')
  if (p.page > 1) s.set('page', String(p.page))
  return s
}

export function apiQuery(p: ListParams, categoryId?: string): string {
  const s = new URLSearchParams()
  if (p.q) s.set('q', p.q)
  if (categoryId) s.set('categoryId', categoryId)
  for (const b of p.brands) s.append('brand', b)
  if (p.minPrice) s.set('minPrice', p.minPrice)
  if (p.maxPrice) s.set('maxPrice', p.maxPrice)
  if (p.inStock) s.set('inStock', 'true')
  s.set('sort', p.sort)
  s.set('page', String(p.page))
  s.set('pageSize', String(PAGE_SIZE))
  return s.toString()
}

/** Marka ve fiyat/stok süzgeçlerinin sayısı (arama ve sıralama hariç). */
export function activeFilterCount(p: ListParams): number {
  return p.brands.length + (p.minPrice || p.maxPrice ? 1 : 0) + (p.inStock ? 1 : 0)
}

export function clearFilters(p: ListParams): ListParams {
  return { ...p, brands: [], minPrice: '', maxPrice: '', inStock: false, page: 1 }
}

export const categoryHref = (slugPath: string): string => `/kategori/${slugPath}`

export function findCategoryBySlug(categories: StoreCategory[], slugPath: string): StoreCategory | undefined {
  return categories.find((c) => c.slugPath === slugPath)
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

/** Adet kutusundaki +/- düğmeleri koli adedi kadar adımlar ve en az adedin altına inmez. */
export function stepQuantity(text: string, direction: -1 | 1, min: number, pack: number): number {
  const p = Math.max(pack, 1)
  const m = effectiveMin(min, p)
  const current = /^\d+$/.test(text.trim()) ? Number(text.trim()) : m
  const snapped = Math.max(m, Math.ceil(current / p) * p) // geçersiz değer önce geçerli kata oturur
  if (direction === 1) return snapped === current ? current + p : snapped
  return Math.max(m, snapped === current ? current - p : snapped - p)
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
