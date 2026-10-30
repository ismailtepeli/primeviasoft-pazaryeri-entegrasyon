import { describe, expect, it } from 'vitest'
import {
  activeFilterCount, apiQuery, categoryHref, categoryTrail, checkQuantity, childrenOf, clearFilters, effectiveMin, emptyParams, findCategoryBySlug, formatMoney,
  parseListParams, quantityNote, stepQuantity, toSearchParams, type StoreCategory,
} from './catalog'

const cats: StoreCategory[] = [
  { id: 'a', name: 'Giyim', parentId: null, productCount: 3, imageUrl: null, slugPath: 'giyim' },
  { id: 'b', name: 'Tişört', parentId: 'a', productCount: 2, imageUrl: null, slugPath: 'giyim/tisort' },
  { id: 'c', name: 'Kırtasiye', parentId: null, productCount: 1, imageUrl: null, slugPath: 'kirtasiye' },
]

describe('catalog', () => {
  it('adres parametrelerini okur; geçersiz sıralama, sayfa ve fiyat varsayılana düşer', () => {
    const p = parseListParams(new URLSearchParams('q= kalem &sort=price&page=3&brand=Nike&brand=Faber&brand=Nike&min=10,5&max=abc&stock=1'))
    expect(p).toEqual({ q: 'kalem', sort: 'price', page: 3, brands: ['Nike', 'Faber'], minPrice: '10.5', maxPrice: '', inStock: true })
    expect(parseListParams(new URLSearchParams('sort=x&page=-2'))).toEqual(emptyParams)
    expect(parseListParams(new URLSearchParams('page=1.5')).page).toBe(1)
  })

  it('adres çubuğuna varsayılanları yazmaz ve okuduğunu aynen geri yazar', () => {
    expect(toSearchParams(emptyParams).toString()).toBe('')
    const p = { q: 'a b', sort: 'price', page: 2, brands: ['Nike', 'Faber'], minPrice: '10', maxPrice: '99.5', inStock: true }
    const qs = toSearchParams(p).toString()
    expect(qs).toBe('q=a+b&sort=price&brand=Nike&brand=Faber&min=10&max=99.5&stock=1&page=2')
    expect(parseListParams(new URLSearchParams(qs))).toEqual(p)
  })

  it('API sorgusu kategori kimliğini ve süzgeçleri taşır', () => {
    const api = new URLSearchParams(apiQuery({ ...emptyParams, q: 'k', sort: 'price', page: 2, brands: ['Nike'], minPrice: '5', inStock: true }, 'x'))
    expect(Object.fromEntries(api)).toEqual({ q: 'k', categoryId: 'x', brand: 'Nike', minPrice: '5', inStock: 'true', sort: 'price', page: '2', pageSize: '24' })
    expect(new URLSearchParams(apiQuery(emptyParams)).has('categoryId')).toBe(false)
  })

  it('süzgeç sayısı ve temizleme sayfayı başa alır, aramayı korur', () => {
    const p = { ...emptyParams, q: 'k', brands: ['A', 'B'], minPrice: '1', maxPrice: '2', inStock: true, page: 4 }
    expect(activeFilterCount(p)).toBe(4)
    expect(clearFilters(p)).toEqual({ ...emptyParams, q: 'k' })
    expect(activeFilterCount(emptyParams)).toBe(0)
  })

  it('kategori adresi ve arama', () => {
    expect(categoryHref('giyim/tisort')).toBe('/kategori/giyim/tisort')
    expect(findCategoryBySlug(cats, 'giyim/tisort')?.id).toBe('b')
    expect(findCategoryBySlug(cats, 'yok')).toBeUndefined()
  })

  it('en az adet koli katına yuvarlanır', () => {
    expect(effectiveMin(6, 4)).toBe(8)
    expect(effectiveMin(0, 0)).toBe(1)
    expect(effectiveMin(10, 5)).toBe(10)
  })

  it('adet doğrulaması sunucu kuralıyla aynı', () => {
    expect(checkQuantity('10', 10, 5)).toEqual({ valid: true, suggested: 10, error: null })
    expect(checkQuantity('5', 10, 5)).toMatchObject({ valid: false, suggested: 10 })
    expect(checkQuantity('12', 10, 5)).toMatchObject({ valid: false, suggested: 15 })
    expect(checkQuantity('4', 6, 4)).toMatchObject({ valid: false, suggested: 8 })
    expect(checkQuantity('', 1, 1).valid).toBe(false)
    expect(checkQuantity('1,5', 1, 1).valid).toBe(false)
    expect(checkQuantity('3', 1, 1).valid).toBe(true)
  })

  it('artı/eksi düğmeleri koli adedi kadar adımlar ve en az adedin altına inmez', () => {
    expect(stepQuantity('10', 1, 10, 5)).toBe(15)
    expect(stepQuantity('15', -1, 10, 5)).toBe(10)
    expect(stepQuantity('10', -1, 10, 5)).toBe(10)
    expect(stepQuantity('12', 1, 10, 5)).toBe(15)   // geçersiz değer önce katına oturur
    expect(stepQuantity('12', -1, 10, 5)).toBe(10)
    expect(stepQuantity('', 1, 1, 1)).toBe(2)
    expect(stepQuantity('abc', 1, 6, 3)).toBe(9)
    expect(stepQuantity('1', -1, 1, 1)).toBe(1)
  })

  it('adet notu yalnızca anlamlı kuralları yazar', () => {
    expect(quantityNote(1, 1)).toBe('')
    expect(quantityNote(10, 5)).toBe('En az 10 adet · 5\'li koli')
    expect(quantityNote(1, 6)).toBe('6\'li koli')
  })

  it('para birimini biçimler', () => {
    expect(formatMoney(1234.5)).toBe('1.234,50 ₺')
    expect(formatMoney(5, 'EUR')).toBe('5,00 €')
  })

  it('kategori yolu ve alt kategoriler', () => {
    expect(categoryTrail(cats, 'b').map((c) => c.name)).toEqual(['Giyim', 'Tişört'])
    expect(categoryTrail(cats, 'zzz')).toEqual([])
    expect(childrenOf(cats, null).map((c) => c.id)).toEqual(['a', 'c'])
    expect(childrenOf(cats, 'a').map((c) => c.id)).toEqual(['b'])
  })
})
