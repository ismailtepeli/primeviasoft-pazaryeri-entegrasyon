export type ProductFilter = 'all' | 'error' | 'pending'

export const PRODUCT_PAGE_SIZE = 20

/** Ürün listesi isteğinin sorgu dizesi; sayfa numarası 1'den başlar. */
export function productsQuery(search: string, filter: ProductFilter, page: number, category = ''): string {
  const params = new URLSearchParams({ page: String(page), pageSize: String(PRODUCT_PAGE_SIZE) })
  if (search.trim()) params.set('q', search.trim())
  if (filter !== 'all') params.set('filter', filter)
  if (category) params.set('category', category)
  return params.toString()
}
