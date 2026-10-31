import { describe, expect, it } from 'vitest'
import type { PriceListRow } from '../api/types'
import { emptyPriceListForm, exportFileName, validateQuantityOverride, formFromList, formatMoney, listTarget, parseCount, parseDecimal, priceImportSummary, toPriceListInput, validatePriceListForm, validateQuantity } from './priceListView'

const row: PriceListRow = {
  id: '1', name: 'Altın', currency: 'TRY', groupId: 'g1', groupName: 'Altın grup', dealerId: null, dealerName: null,
  discountPercent: 7.5, pricesIncludeVat: false, showVatIncluded: true, isActive: true, itemCount: 3,
}

describe('priceListView', () => {
  it('parseDecimal virgül ve noktayı kabul eder, geçersizi reddeder', () => {
    expect(parseDecimal('12,5')).toBe(12.5)
    expect(parseDecimal(' 7.25 ')).toBe(7.25)
    expect(parseDecimal('')).toBeNull()
    expect(parseDecimal('abc')).toBeNull()
    expect(parseDecimal('-3')).toBeNull()
  })

  it('parseCount yalnızca tam sayı kabul eder', () => {
    expect(parseCount('10')).toBe(10)
    expect(parseCount('1,5')).toBeNull()
    expect(parseCount('')).toBeNull()
  })

  it('formatMoney para birimi simgesini ekler', () => {
    expect(formatMoney(1234.5)).toBe('1.234,50 ₺')
    expect(formatMoney(5, 'USD')).toBe('5,00 $')
  })

  it('listTarget bağlamı yazar', () => {
    expect(listTarget(row)).toBe('Grup: Altın grup')
    expect(listTarget({ groupName: null, dealerName: 'Mavi' })).toBe('Bayi: Mavi')
    expect(listTarget({ groupName: null, dealerName: null })).toBe('Bağlı değil')
  })

  it('form doğrulaması ad, hedef ve iskontoyu denetler', () => {
    expect(validatePriceListForm(emptyPriceListForm)).toEqual({ name: 'Liste adı boş olamaz.', targetId: 'Bir bayi grubu seçin.' })
    expect(validatePriceListForm({ ...emptyPriceListForm, name: 'x', targetKind: 'none', discount: '101' })).toHaveProperty('discount')
    expect(validatePriceListForm({ ...emptyPriceListForm, name: 'x', targetKind: 'dealer' })).toHaveProperty('targetId', 'Bir bayi seçin.')
    expect(validatePriceListForm({ ...emptyPriceListForm, name: 'x', targetKind: 'none', discount: '12,5' })).toEqual({})
  })

  it('toPriceListInput hedefi doğru alana yazar', () => {
    const f = { ...formFromList(row), targetKind: 'dealer' as const, targetId: 'd1' }
    expect(toPriceListInput(f)).toMatchObject({ groupId: null, dealerId: 'd1', discountPercent: 7.5 })
    expect(toPriceListInput(formFromList(row))).toMatchObject({ groupId: 'g1', dealerId: null })
    expect(formFromList(row).discount).toBe('7,5')
  })

  it('validateQuantity en az 1 tam sayı ister', () => {
    expect(validateQuantity('10', '5')).toBeNull()
    expect(validateQuantity('0', '5')).not.toBeNull()
    expect(validateQuantity('1', '')).not.toBeNull()
    expect(validateQuantity('100001', '1')).not.toBeNull()
  })

  it('priceImportSummary atlananı yalnızca varsa yazar', () => {
    expect(priceImportSummary({ created: 2, updated: 1, skipped: 0 })).toBe('2 yeni, 1 güncellendi.')
    expect(priceImportSummary({ created: 0, updated: 0, skipped: 3 })).toBe('0 yeni, 0 güncellendi, 3 satır atlandı.')
  })

  it('dışa aktarılan dosyanın adı liste adından güvenli üretilir', () => {
    expect(exportFileName('Altın Liste')).toBe('fiyat-listesi-altın-liste.xlsx')
    expect(exportFileName('Gümüş / Özel 2027!')).toBe('fiyat-listesi-gümüş-özel-2027.xlsx')
    expect(exportFileName('  ')).toBe('fiyat-listesi-liste.xlsx')
    expect(exportFileName(undefined)).toBe('fiyat-listesi-liste.xlsx')
  })

  it('liste kalemi adet geçersiz kılması: boş serbest, dolu alan 1-100000 tam sayı', () => {
    expect(validateQuantityOverride('', '')).toBeNull()
    expect(validateQuantityOverride('10', '')).toBeNull()
    expect(validateQuantityOverride('', '6')).toBeNull()
    expect(validateQuantityOverride(' 10 ', '5')).toBeNull()
    expect(validateQuantityOverride('0', '')).toMatch(/Minimum/)
    expect(validateQuantityOverride('', 'abc')).toMatch(/Koli/)
    expect(validateQuantityOverride('100001', '')).toMatch(/Minimum/)
    expect(validateQuantityOverride('2,5', '')).not.toBeNull()
  })
})
