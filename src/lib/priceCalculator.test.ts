import { describe, expect, it } from 'vitest'
import { calculatePrice, roundPrice, validateRule, type PriceRounding } from './priceCalculator'

const rule = (commissionPercent: number, shippingCost: number, profitPercent: number, rounding: PriceRounding) => ({ commissionPercent, shippingCost, profitPercent, rounding })

// Aynı vektörler backend PricingTests.Fiyat_formule_ve_yuvarlamaya_gore_hesaplanir içinde de var.
describe('calculatePrice', () => {
  it.each([
    [100, rule(15, 10, 20, 'None'), 155.29],
    [100, rule(15, 10, 20, 'NearestWhole'), 155],
    [100, rule(15, 10, 20, 'UpWhole'), 156],
    [100, rule(15, 10, 20, 'Ending90'), 155.9],
    [100, rule(15, 10, 20, 'Ending99'), 155.99],
    [200, rule(10, 0, 0, 'None'), 222.22],
    [200, rule(10, 0, 0, 'Ending90'), 222.9],
    [50, rule(0, 0, 0, 'None'), 50],
    [80, rule(0, 0, 25, 'UpWhole'), 100],
  ])('maliyet %s için fiyat', (cost, r, expected) => {
    expect(calculatePrice(cost, r)?.price).toBe(expected)
  })

  it('dökümü ve net kârı verir', () => {
    expect(calculatePrice(100, rule(15, 10, 20, 'None'))).toEqual({ base: 110, withProfit: 132, beforeRounding: 155.29, price: 155.29, commissionAmount: 23.29, netProfit: 22 })
  })

  it('geçersiz girdide null döner', () => {
    expect(calculatePrice(0, rule(10, 0, 0, 'None'))).toBeNull()
    expect(calculatePrice(10, rule(100, 0, 0, 'None'))).toBeNull()
    expect(calculatePrice(Number.NaN, rule(10, 0, 0, 'None'))).toBeNull()
  })
})

describe('roundPrice', () => {
  it.each([
    [222.95, 'Ending90', 223.9],
    [222.9, 'Ending90', 222.9],
    [222.91, 'Ending90', 223.9],
    [222.99, 'Ending99', 222.99],
    [222.0, 'Ending99', 222.99],
    [222.5, 'NearestWhole', 223],
    [222.001, 'UpWhole', 223],
    [222.901, 'Ending90', 223.9],
  ] as [number, PriceRounding, number][])('%s %s → %s', (value, rounding, expected) => {
    expect(roundPrice(value, rounding)).toBe(expected)
  })
})

describe('validateRule', () => {
  it('sınırları denetler', () => {
    expect(validateRule(rule(99.99, 0, 1000, 'None'))).toBeNull()
    expect(validateRule(rule(-1, 0, 0, 'None'))).toContain('Komisyon')
    expect(validateRule(rule(10, 0, 5000, 'None'))).toContain('Kâr')
    expect(validateRule(rule(10, -5, 0, 'None'))).toContain('Kargo')
  })
})
