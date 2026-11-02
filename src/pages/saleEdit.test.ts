import { describe, expect, it } from 'vitest'
import { saleSummary, salePriceFor, validateSale } from './saleEdit'

describe('saleEdit', () => {
  it('kampanya fiyatını sunucuyla aynı hesaplar', () => {
    expect(salePriceFor('percent', 25, 100)).toBe(75)
    expect(salePriceFor('percent', 10, 199.9)).toBe(179.91)
    expect(salePriceFor('price', 80, 100)).toBe(80)
    expect(salePriceFor('price', 100, 100)).toBeNull() // normal fiyata eşit
    expect(salePriceFor('price', 150, 100)).toBeNull() // normalden yüksek
    expect(salePriceFor('clear', 0, 100)).toBeNull()
  })

  it('girdiyi doğrular', () => {
    expect(validateSale('percent', '12,5')).toEqual({ value: 12.5 })
    expect(validateSale('percent', '0')).toHaveProperty('error')
    expect(validateSale('percent', '91')).toHaveProperty('error')
    expect(validateSale('price', '0')).toHaveProperty('error')
    expect(validateSale('price', 'abc')).toEqual({ error: 'Bir sayı girin.' })
    expect(validateSale('clear', '')).toEqual({ value: 0 })
  })

  it('özet cümlesi', () => {
    expect(saleSummary({ updated: 2, cleared: 0, skipped: [] })).toBe('2 ürüne kampanya uygulandı.')
    expect(saleSummary({ updated: 1, cleared: 0, skipped: ['A'] })).toContain('1 ürün atlandı')
    expect(saleSummary({ updated: 0, cleared: 3, skipped: [] })).toBe('3 ürünün kampanyası kaldırıldı.')
    expect(saleSummary({ updated: 0, cleared: 0, skipped: [] })).toBe('Değişen ürün olmadı.')
  })
})
