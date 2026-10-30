import { describe, expect, it } from 'vitest'
import { apiQuery, categoryTrail, checkQuantity, childrenOf, effectiveMin, formatMoney, parseListParams, quantityNote, toSearchParams, type StoreCategory } from './catalog'

const cats: StoreCategory[] = [
  { id: 'a', name: 'Giyim', parentId: null, productCount: 3 },
  { id: 'b', name: 'Tişört', parentId: 'a', productCount: 2 },
  { id: 'c', name: 'Kırtasiye', parentId: null, productCount: 1 },
]

describe('catalog', () => {
  it('adres parametrelerini okur; geçersiz sıralama ve sayfa varsayılana düşer', () => {
    expect(parseListParams(new URLSearchParams('q= kalem &category=a&sort=price&page=3'))).toEqual({ q: 'kalem', category: 'a', sort: 'price', page: 3 })
    expect(parseListParams(new URLSearchParams('sort=x&page=-2'))).toEqual({ q: '', category: '', sort: 'name', page: 1 })
    expect(parseListParams(new URLSearchParams('page=1.5')).page).toBe(1)
  })

  it('adres çubuğuna varsayılanları yazmaz, API sorgusunu kurar', () => {
    expect(toSearchParams({ q: '', category: '', sort: 'name', page: 1 }).toString()).toBe('')
    expect(toSearchParams({ q: 'a b', category: 'x', sort: 'price', page: 2 }).toString()).toBe('q=a+b&category=x&sort=price&page=2')
    const api = new URLSearchParams(apiQuery({ q: 'k', category: 'x', sort: 'price', page: 2 }))
    expect(Object.fromEntries(api)).toEqual({ q: 'k', categoryId: 'x', sort: 'price', page: '2', pageSize: '24' })
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
