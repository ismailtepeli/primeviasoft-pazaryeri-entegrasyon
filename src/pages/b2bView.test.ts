import { describe, expect, it } from 'vitest'
import { parseCount, stockDisplayOptions, vatDisplayOptions } from './b2bView'

describe('b2bView', () => {
  it('parseCount yalnızca rakam kabul eder', () => {
    expect(parseCount('5')).toBe(5)
    expect(parseCount(' 0 ')).toBe(0)
    expect(parseCount('-1')).toBeNull()
    expect(parseCount('')).toBeNull()
    expect(parseCount('1,5')).toBeNull()
  })

  it('stok gösterim seçenekleri sunucu kipleriyle aynıdır', () => {
    expect(stockDisplayOptions.map((o) => o.value).sort()).toEqual(['Exact', 'Hidden', 'Level'])
  })

  it('genel fiyat gösterimi dahil/hariç seçenekleri', () => {
    expect(vatDisplayOptions.map((o) => o.value)).toEqual(['true', 'false'])
    expect(vatDisplayOptions.map((o) => o.label)).toEqual(['KDV dahil', 'KDV hariç'])
  })
})
