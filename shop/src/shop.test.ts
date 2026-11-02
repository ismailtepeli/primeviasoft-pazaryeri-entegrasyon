import { describe, expect, it } from 'vitest'
import {
  activeFilterCount, apiQuery, categoryHref, categoryTrail, childrenOf, clearFilters, discountPercent, emptyParams, findCategoryBySlug, formatMoney,
  parseListParams, stockTone, toSearchParams, type ShopCategory,
} from './shop'

const cat = (id: string, parentId: string | null, slugPath: string): ShopCategory => ({ id, name: id.toUpperCase(), parentId, productCount: 1, imageUrl: null, slugPath })
const categories = [cat('a', null, 'giyim'), cat('b', 'a', 'giyim/tisort'), cat('c', null, 'ev')]

describe('shop', () => {
  it('adres parametrelerini okur ve yazar; varsayılanlar yazılmaz', () => {
    const p = parseListParams(new URLSearchParams('q= tişört &sort=price&page=3&brand=Demo&brand=Demo&brand=Ev&min=10,5&max=abc&stock=1'))
    expect(p).toEqual({ q: 'tişört', sort: 'price', page: 3, brands: ['Demo', 'Ev'], minPrice: '10.5', maxPrice: '', inStock: true })
    expect(toSearchParams(p).toString()).toBe('q=ti%C5%9F%C3%B6rt&sort=price&brand=Demo&brand=Ev&min=10.5&stock=1&page=3')
    expect(toSearchParams(emptyParams).toString()).toBe('')
    expect(parseListParams(new URLSearchParams('sort=zzz&page=-2')).sort).toBe('name')
    expect(parseListParams(new URLSearchParams('page=-2')).page).toBe(1)
  })

  it('API sorgusu süzgeçleri ve sayfa boyutunu içerir', () => {
    const q = new URLSearchParams(apiQuery({ ...emptyParams, brands: ['Demo'], inStock: true, minPrice: '5' }, 'cat-1'))
    expect(q.get('categoryId')).toBe('cat-1')
    expect(q.getAll('brand')).toEqual(['Demo'])
    expect([q.get('inStock'), q.get('minPrice'), q.get('pageSize')]).toEqual(['true', '5', '24'])
  })

  it('süzgeç sayısı ve temizleme', () => {
    const p = { ...emptyParams, brands: ['A', 'B'], minPrice: '1', maxPrice: '9', inStock: true, q: 'x', page: 4 }
    expect(activeFilterCount(p)).toBe(4) // 2 marka + fiyat aralığı + stok
    expect(clearFilters(p)).toMatchObject({ brands: [], minPrice: '', maxPrice: '', inStock: false, page: 1, q: 'x' })
  })

  it('kategori yardımcıları', () => {
    expect(categoryHref('giyim/tisort')).toBe('/kategori/giyim/tisort')
    expect(findCategoryBySlug(categories, 'giyim/tisort')?.id).toBe('b')
    expect(categoryTrail(categories, 'b').map((c) => c.id)).toEqual(['a', 'b'])
    expect(childrenOf(categories, null).map((c) => c.id)).toEqual(['a', 'c'])
  })

  it('para ve indirim yüzdesi', () => {
    expect(formatMoney(1234.5)).toBe('1.234,50 ₺')
    expect(discountPercent(80, 100)).toBe(20)
    expect(discountPercent(99.9, 100)).toBeNull() // %1'in altı gösterilmez
    expect(discountPercent(100, 100)).toBeNull()
    expect(discountPercent(50, null)).toBeNull()
    expect(discountPercent(200, 100)).toBeNull()
  })

  it('stok görünümü', () => {
    expect([stockTone(false, 'Tükendi'), stockTone(true, 'Az kaldı'), stockTone(true, 'Stokta var')]).toEqual(['out', 'low', 'ok'])
  })
})
