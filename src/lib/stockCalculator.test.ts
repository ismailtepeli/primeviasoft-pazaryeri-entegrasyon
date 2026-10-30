import { describe, expect, it } from 'vitest'
import { channelQuantity, effectiveSafety, validateStockRule } from './stockCalculator'

// Aynı vektörler backend StockTests.Kanala_acilan_miktar_... içinde de var.
describe('channelQuantity', () => {
  it.each([
    [100, 0, 100, 100],
    [100, 10, 100, 90],
    [100, 10, 50, 45],
    [7, 0, 50, 3],
    [5, 10, 100, 0],
    [0, 0, 100, 0],
    [100, 0, 0, 0],
    [1000, 100, 33.33, 299],
  ])('stok %s, güvenlik %s, pay %s → %s', (stock, safety, percent, expected) => {
    expect(channelQuantity(stock, safety, percent)).toBe(expected)
  })
})

describe('effectiveSafety', () => {
  it('ürüne özel değer kanal varsayılanını ezer; 0 da geçerli bir değerdir', () => {
    expect(effectiveSafety(3, 10)).toBe(3)
    expect(effectiveSafety(0, 10)).toBe(0)
    expect(effectiveSafety(null, 10)).toBe(10)
  })
})

describe('validateStockRule', () => {
  it('sınırları denetler', () => {
    expect(validateStockRule(0, 100)).toBeNull()
    expect(validateStockRule(-1, 100)).toContain('Güvenlik')
    expect(validateStockRule(1.5, 100)).toContain('Güvenlik')
    expect(validateStockRule(0, 101)).toContain('pay')
    expect(validateStockRule(0, Number.NaN)).toContain('pay')
  })
})
