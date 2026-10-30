import { describe, expect, it } from 'vitest'
import { movementLabel, oversoldNote, signedQuantity } from './stockMovements'

describe('stockMovements', () => {
  it('adedi işaretli yazar', () => {
    expect(signedQuantity(15)).toBe('+15')
    expect(signedQuantity(-3)).toBe('−3')
    expect(signedQuantity(0)).toBe('0')
  })

  it('fazla satışı uyarır', () => {
    expect(oversoldNote({ oversold: 0 })).toBeNull()
    expect(oversoldNote({ oversold: 3 })).toContain('3 adet düşülemedi')
  })

  it('her tür için etiket vardır', () => {
    expect(Object.values(movementLabel).map((l) => l.text)).toEqual(['Satış', 'İptal / iade', 'Düzeltme'])
  })
})
