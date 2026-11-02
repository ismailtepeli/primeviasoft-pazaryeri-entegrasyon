export interface ShopProduct {
  id: string
  name: string
  brand: string | null
  categoryPath: string | null
  coverUrl: string | null
  /** Satış fiyatı (KDV dahil); kampanya varsa kampanya fiyatı. */
  price: number
  /** Kampanya varsa üstü çizili normal fiyat. */
  listPrice: number | null
  currency: string
  inStock: boolean
  stockLabel: string
}

export interface BrandFacet { name: string; count: number }

export interface ShopFacets { brands: BrandFacet[]; priceMin: number | null; priceMax: number | null }

export interface ShopProductPage { items: ShopProduct[]; total: number; facets: ShopFacets }

export interface ShopCategory { id: string; name: string; parentId: string | null; productCount: number; imageUrl: string | null; slugPath: string }

export interface ShopProductDetail {
  id: string
  name: string
  brand: string | null
  description: string | null
  breadcrumb: { id: string; name: string; slugPath: string }[]
  images: string[]
  price: number
  listPrice: number | null
  currency: string
  vatRate: number
  inStock: boolean
  stockLabel: string
  variants: { id: string; variant: string | null; inStock: boolean }[]
}

export const PAGE_SIZE = 24

export const sortOptions = [
  { value: 'name', label: 'Ada göre (A-Z)' },
  { value: 'name_desc', label: 'Ada göre (Z-A)' },
  { value: 'price', label: 'Fiyat (artan)' },
  { value: 'price_desc', label: 'Fiyat (azalan)' },
] as const

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

/** API sorgusu (slug dahil değil; istemci ekler). */
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

export function findCategoryBySlug(categories: ShopCategory[], slugPath: string): ShopCategory | undefined {
  return categories.find((c) => c.slugPath === slugPath)
}

const symbols: Record<string, string> = { TRY: '₺', USD: '$', EUR: '€' }

export function formatMoney(amount: number, currency = 'TRY'): string {
  return `${new Intl.NumberFormat('tr-TR', { minimumFractionDigits: 2, maximumFractionDigits: 2 }).format(amount)} ${symbols[currency] ?? currency}`
}

/** Kampanyadaki indirim yüzdesi (yuvarlanmış); kampanya yoksa ya da anlamsızsa null. */
export function discountPercent(price: number, listPrice: number | null): number | null {
  if (listPrice === null || listPrice <= 0 || price >= listPrice) return null
  const percent = Math.round((1 - price / listPrice) * 100)
  return percent >= 1 ? percent : null
}

/** Kategori kimliğinden köke giden yol (kırıntı için), kökten başlayarak. */
export function categoryTrail(categories: ShopCategory[], id: string): ShopCategory[] {
  const trail: ShopCategory[] = []
  for (let cur = categories.find((c) => c.id === id); cur && trail.length < 20; cur = cur.parentId ? categories.find((c) => c.id === cur!.parentId) : undefined) trail.unshift(cur)
  return trail
}

export function childrenOf(categories: ShopCategory[], parentId: string | null): ShopCategory[] {
  return categories.filter((c) => c.parentId === parentId)
}

/** Stok etiketinin görünümü: tükendi / az kaldı / var. */
export function stockTone(inStock: boolean, label: string): 'out' | 'low' | 'ok' {
  return !inStock ? 'out' : label === 'Az kaldı' ? 'low' : 'ok'
}
