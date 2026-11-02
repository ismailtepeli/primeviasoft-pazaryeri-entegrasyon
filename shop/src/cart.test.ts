import { describe, expect, it } from 'vitest'
import { MAX_LINES, MAX_QUANTITY, addLine, cartCount, fixedQuantity, parseStored, removeLine, setQuantity, totalWith, type QuoteLine } from './cart'

const A = '11111111-1111-1111-1111-111111111111'
const B = '22222222-2222-2222-2222-222222222222'

describe('sepet satırları', () => {
  it('aynı ürünü eklemek adedi toplar, yeni ürün satır açar', () => {
    const lines = addLine(addLine([], A), A, 2)
    expect(lines).toEqual([{ productId: A, quantity: 3 }])
    expect(addLine(lines, B)).toHaveLength(2)
  })

  it('adet üst sınırı aşılmaz ve sıfır adet satır açmaz', () => {
    expect(addLine([{ productId: A, quantity: MAX_QUANTITY }], A)[0].quantity).toBe(MAX_QUANTITY)
    expect(addLine([], A, 0)).toEqual([])
    expect(addLine([], A, -4)).toEqual([])
  })

  it('en fazla satır sayısından sonra yeni ürün eklenmez, mevcut satır artar', () => {
    const full = Array.from({ length: MAX_LINES }, (_, i) => ({ productId: `${i}`.padStart(8, '0') + '-0000-0000-0000-000000000000', quantity: 1 }))
    expect(addLine(full, A)).toHaveLength(MAX_LINES)
    expect(addLine(full, full[0].productId)[0].quantity).toBe(2)
  })

  it('setQuantity belirler, sıfır ve altı satırı kaldırır', () => {
    const lines = [{ productId: A, quantity: 2 }, { productId: B, quantity: 1 }]
    expect(setQuantity(lines, A, 5)[0].quantity).toBe(5)
    expect(setQuantity(lines, A, 0)).toEqual([{ productId: B, quantity: 1 }])
    expect(setQuantity(lines, A, -1)).toHaveLength(1)
    expect(setQuantity(lines, A, 1000)[0].quantity).toBe(MAX_QUANTITY)
    expect(removeLine(lines, B)).toEqual([{ productId: A, quantity: 2 }])
    expect(cartCount(lines)).toBe(3)
  })
})

describe('saklanan sepeti okuma', () => {
  it('geçerli sepeti okur', () => {
    expect(parseStored(JSON.stringify([{ productId: A, quantity: 2 }]))).toEqual([{ productId: A, quantity: 2 }])
  })

  it('bozuk, boş ya da elle değiştirilmiş veriyi eler', () => {
    expect(parseStored(null)).toEqual([])
    expect(parseStored('{bozuk')).toEqual([])
    expect(parseStored('{"a":1}')).toEqual([])
    const dirty = [{ productId: 'x', quantity: 1 }, { productId: A, quantity: 0 }, { productId: A, quantity: 'abc' }, { productId: B, quantity: 500 }, { productId: B, quantity: 1 }, null, 7]
    expect(parseStored(JSON.stringify(dirty))).toEqual([{ productId: B, quantity: MAX_QUANTITY }])
  })
})

describe('sunucu fiyatlaması yardımcıları', () => {
  const line = (issue: QuoteLine['issue'], quantity = 5): QuoteLine => ({ productId: A, name: 'Ürün', coverUrl: null, quantity, unitPrice: 10, listPrice: null, lineTotal: 50, issue })

  it('stok sorununda stoktaki adedi önerir, satışta olmayanı ve tükeneni kaldırtır', () => {
    expect(fixedQuantity(line(null))).toBe(5)
    expect(fixedQuantity(line({ kind: 'stock', text: '', available: 3 }))).toBe(3)
    expect(fixedQuantity(line({ kind: 'stock', text: '', available: 0 }))).toBeNull()
    expect(fixedQuantity(line({ kind: 'unavailable', text: '', available: null }))).toBeNull()
  })

  it('seçilen ödeme yönteminin ücretini toplama ekler', () => {
    const quote = { total: 125.5, paymentOptions: [{ method: 'BankTransfer', label: '', fee: 0, info: null }, { method: 'CashOnDelivery', label: '', fee: 15, info: null }] }
    expect(totalWith(quote, 'BankTransfer')).toBe(125.5)
    expect(totalWith(quote, 'CashOnDelivery')).toBe(140.5)
    expect(totalWith(quote, null)).toBe(125.5)
  })
})
