import { describe, expect, it } from 'vitest'
import { imageHint, moveItem } from './imageOrder'

describe('moveItem', () => {
  it('öğeyi taşır', () => {
    expect(moveItem(['a', 'b', 'c'], 2, 0)).toEqual(['c', 'a', 'b'])
    expect(moveItem(['a', 'b', 'c'], 0, 1)).toEqual(['b', 'a', 'c'])
  })

  it('sınır dışı ya da aynı konumda listeyi değiştirmez', () => {
    const list = ['a', 'b']
    expect(moveItem(list, 0, 0)).toBe(list)
    expect(moveItem(list, 0, 5)).toBe(list)
    expect(moveItem(list, -1, 1)).toBe(list)
  })

  it('özgün diziyi bozmaz', () => {
    const list = ['a', 'b', 'c']
    moveItem(list, 0, 2)
    expect(list).toEqual(['a', 'b', 'c'])
  })
})

describe('imageHint', () => {
  it('3 görselden azında uyarır, yeterliyse susar', () => {
    expect(imageHint(0)).toContain('Hiç görsel yok')
    expect(imageHint(2)).toContain('2 görsel var')
    expect(imageHint(3)).toBeNull()
    expect(imageHint(8)).toBeNull()
  })
})
