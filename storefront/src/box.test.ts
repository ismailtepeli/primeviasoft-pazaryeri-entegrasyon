import { describe, expect, it } from 'vitest'
import { fromBoxText, quantityLabel, toBoxText } from './catalog'

describe('koli gösterimi', () => {
  it('koli adedi 1 ise adet olduğu gibi kalır', () => {
    expect(toBoxText('7', 1)).toBe('7')
    expect(fromBoxText('7', 1)).toBe('7')
  })

  it('12 lik kolide kutu koli sayar, sunucuya adet gider', () => {
    expect(toBoxText('12', 12)).toBe('1')
    expect(toBoxText('36', 12)).toBe('3')
    expect(fromBoxText('1', 12)).toBe('12')
    expect(fromBoxText('3', 12)).toBe('36')
  })

  it('geçersiz metin olduğu gibi döner ve koli katı olmayan adet çevrilmez', () => {
    expect(fromBoxText('', 12)).toBe('')
    expect(fromBoxText('abc', 12)).toBe('abc')
    expect(fromBoxText('2,5', 12)).toBe('2,5')
    expect(toBoxText('13', 12)).toBe('13')
    expect(toBoxText('', 12)).toBe('')
    expect(toBoxText('0', 12)).toBe('0')
  })

  it('gidiş dönüş tutarlı', () => {
    for (const n of [1, 2, 5, 40]) expect(toBoxText(fromBoxText(String(n), 6), 6)).toBe(String(n))
  })

  it('etiket koli ve adedi birlikte söyler', () => {
    expect(quantityLabel(24, 12)).toBe('2 koli · 24 adet')
    expect(quantityLabel(5, 1)).toBe('5 adet')
    expect(quantityLabel(13, 12)).toBe('13 adet')
  })
})
