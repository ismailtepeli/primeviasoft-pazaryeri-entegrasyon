import { describe, expect, it } from 'vitest'
import { orderStatusText, orderSteps, priceChangeNote, type CartLine } from './orders'

const line = (over: Partial<CartLine>): CartLine => ({
  productId: 'p', name: 'Kalem', code: 'PEN', coverUrl: null, quantity: 10, price: { amount: 12, includesVat: true, currency: 'TRY', vatRate: 20 }, lineTotal: 120,
  minOrderQuantity: 1, packSize: 1, stock: { quantity: null, label: 'Stokta var', inStock: true }, issue: null, priceAtAdd: 12, ...over,
})

describe('orders', () => {
  it('durum çubuğu akıştaki yeri gösterir', () => {
    expect(orderSteps('New').map((s) => s.state)).toEqual(['current', 'todo', 'todo', 'todo'])
    expect(orderSteps('Shipped').map((s) => s.state)).toEqual(['done', 'done', 'current', 'todo'])
    expect(orderSteps('Delivered').map((s) => s.state)).toEqual(['done', 'done', 'done', 'current'])
  })

  it('iptal ve iade tek adımdır', () => {
    expect(orderSteps('Cancelled')).toEqual([{ label: 'İptal edildi', state: 'current' }])
    expect(orderSteps('Returned')).toHaveLength(1)
  })

  it('bilinmeyen durum olduğu gibi yazılır', () => {
    expect(orderStatusText('New')).toBe('Onay bekliyor')
    expect(orderStatusText('Preparing')).toContain('Onaylandı')
    expect(orderStatusText('Xyz')).toBe('Xyz')
  })

  it('fiyat değişimi uyarısı', () => {
    expect(priceChangeNote(line({}))).toBeNull()
    expect(priceChangeNote(line({ priceAtAdd: 10 }))).toBe('Fiyat arttı')
    expect(priceChangeNote(line({ priceAtAdd: 15 }))).toBe('Fiyat düştü')
    expect(priceChangeNote(line({ priceAtAdd: null }))).toBeNull()
    expect(priceChangeNote(line({ priceAtAdd: 10, issue: { kind: 'unavailable', text: 'x' } }))).toBeNull()
  })
})

describe('sepet sayacı', () => {
  it('koli adedi olan ürünler koli, diğerleri adet sayılır', async () => {
    const { cartUnits, cartSummary } = await import('./orders')
    expect(cartUnits([{ quantity: 24, packSize: 12 }])).toBe(2)
    expect(cartUnits([{ quantity: 24, packSize: 12 }, { quantity: 5, packSize: 1 }])).toBe(7)
    expect(cartUnits([{ quantity: 13, packSize: 12 }])).toBe(13) // koli katı değilse adet
    expect(cartUnits([])).toBe(0)
    expect(cartSummary([{ quantity: 24, packSize: 12 }, { quantity: 5, packSize: 1 }])).toBe('2 koli · 29 adet')
    expect(cartSummary([{ quantity: 5, packSize: 1 }])).toBe('5 adet')
  })
})
