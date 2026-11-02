import { describe, expect, it } from 'vitest'
import { applyBulk, bulkSummary, parsePriceText, priceImportSummary, validateBulk, validateSinglePrice } from './priceEdit'

describe('priceEdit', () => {
  it('Türkçe ve İngilizce yazılmış fiyatı okur', () => {
    expect(parsePriceText('12,5')).toBe(12.5)
    expect(parsePriceText('1.250,50')).toBe(1250.5)
    expect(parsePriceText('1,250.50')).toBe(1250.5)
    expect(parsePriceText(' 99 ₺ ')).toBe(99)
    expect(parsePriceText('-5')).toBe(-5)
    expect(parsePriceText('abc')).toBeNull()
    expect(parsePriceText('')).toBeNull()
  })

  it('tek ürün fiyatını yuvarlar ve geçersizi reddeder', () => {
    expect(validateSinglePrice('12,345')).toEqual({ price: 12.35 })
    expect(validateSinglePrice('0')).toEqual({ error: 'Fiyat sıfırdan büyük olmalı.' })
    expect(validateSinglePrice('x')).toEqual({ error: 'Fiyat sayı olmalı.' })
    expect(validateSinglePrice('10000000000')).toEqual({ error: 'Fiyat çok büyük.' })
  })

  it('toplu kuralı sunucuyla aynı hesaplar', () => {
    expect(applyBulk('percent', 10, 10)).toBe(11)
    expect(applyBulk('percent', -25, 12)).toBe(9)
    expect(applyBulk('amount', -1.5, 10)).toBe(8.5)
    expect(applyBulk('amount', -10, 10)).toBeNull() // sıfıra düşer
    expect(applyBulk('set', 110, 5)).toBe(110)
  })

  it('toplu girdiyi doğrular', () => {
    expect(validateBulk('percent', '12,5')).toEqual({ value: 12.5 })
    expect(validateBulk('percent', '-95')).toHaveProperty('error')
    expect(validateBulk('percent', '1001')).toHaveProperty('error')
    expect(validateBulk('set', '0')).toHaveProperty('error')
    expect(validateBulk('amount', '-3')).toEqual({ value: -3 })
    expect(validateBulk('amount', '')).toHaveProperty('error')
  })

  it('özet cümleleri yalnızca anlamlı parçaları içerir', () => {
    expect(bulkSummary({ updated: 3, unchanged: 0, skipped: [] })).toBe('3 ürünün fiyatı güncellendi.')
    expect(bulkSummary({ updated: 2, unchanged: 1, skipped: [{}] })).toContain('1 ürün atlandı')
    expect(priceImportSummary({ updated: 2, unchanged: 5, errors: [] })).toBe('2 ürünün fiyatı güncellendi, 5 ürün zaten güncel.')
    expect(priceImportSummary({ updated: 0, unchanged: 0, errors: [1, 2] })).toContain('2 satır hatalı')
  })
})
