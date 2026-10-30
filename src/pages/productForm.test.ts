import { describe, expect, it } from 'vitest'
import { emptyForm, toInput, validateForm } from './productForm'

const valid = { ...emptyForm, name: ' Çanta ', barcode: 'B1', stock: '5', price: '99,90' }

describe('productForm', () => {
  it('geçerli formda hata yok ve girdi temizlenir', () => {
    expect(validateForm(valid)).toEqual({})
    expect(toInput(valid)).toEqual({
      name: 'Çanta', barcode: 'B1', mainId: null, variant: null, brand: null, description: null, categoryId: null, stock: 5, price: 99.9, vatRate: 20, cost: null,
    })
  })

  it('zorunlu alanları ve sayı kurallarını denetler', () => {
    const errors = validateForm({ ...emptyForm, stock: '1.5', price: '0' })
    expect(Object.keys(errors).sort()).toEqual(['barcode', 'name', 'price', 'stock'])
    expect(validateForm({ ...valid, stock: '-1' }).stock).toBeDefined()
    expect(validateForm({ ...valid, price: 'abc' }).price).toBeDefined()
    expect(validateForm({ ...valid, cost: '0' }).cost).toBeDefined()
    expect(validateForm({ ...valid, cost: '60,5' }).cost).toBeUndefined()
    expect(toInput({ ...valid, cost: '60,5' }).cost).toBe(60.5)
  })

  it('uzunluk sınırlarını uygular', () => {
    expect(validateForm({ ...valid, name: 'x'.repeat(501) }).name).toBeDefined()
    expect(validateForm({ ...valid, description: 'x'.repeat(5001) }).description).toBeDefined()
  })
})
