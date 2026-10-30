import { describe, expect, it } from 'vitest'
import type { Category } from '../api/types'
import { indentedLabel, movedOrder, moveTargets, siblingsOf, slugLabel, visibleRows } from './categoryTree'

const c = (id: string, name: string, parentId: string | null): Category => ({
  id, name, parentId, path: name, productCount: 0, totalProductCount: 0, sortOrder: 0, isActive: true, imageUrl: null, slugPath: name.toLowerCase(), childCount: 0,
})

// A > B > C ; A > D ; E
const all = [c('A', 'A', null), c('B', 'B', 'A'), c('C', 'C', 'B'), c('D', 'D', 'A'), c('E', 'E', null)]

describe('categoryTree', () => {
  it('görünen satırlar derinlik ve alt dal bilgisini taşır', () => {
    const rows = visibleRows(all, new Set())
    expect(rows.map((r) => [r.category.id, r.depth, r.hasChildren])).toEqual([['A', 0, true], ['B', 1, true], ['C', 2, false], ['D', 1, false], ['E', 0, false]])
  })

  it('kapalı dalın tüm torunları gizlenir', () => {
    expect(visibleRows(all, new Set(['A'])).map((r) => r.category.id)).toEqual(['A', 'E'])
    expect(visibleRows(all, new Set(['B'])).map((r) => r.category.id)).toEqual(['A', 'B', 'D', 'E'])
  })

  it('kardeşler ve sıra değişimi', () => {
    expect(siblingsOf(all, all[1]).map((x) => x.id)).toEqual(['B', 'D'])
    expect(movedOrder(all, all[1], 1)).toEqual(['D', 'B'])
    expect(movedOrder(all, all[3], -1)).toEqual(['D', 'B'])
    expect(movedOrder(all, all[1], -1)).toBeNull()
    expect(movedOrder(all, all[4], 1)).toBeNull()
  })

  it('taşıma hedefleri kendini, alt dalını, mevcut üstünü ve derinliği aşanları dışlar', () => {
    expect(moveTargets(all, all[0]).map((x) => x.id)).toEqual(['E']) // A, B, C, D alt dalı
    expect(moveTargets(all, all[2]).map((x) => x.id)).toEqual(['A', 'D', 'E']) // C: mevcut üst B hariç
    expect(moveTargets(all, all[0], 3).map((x) => x.id)).toEqual([]) // A dalı 3 seviye; E altına konursa 4 olur
  })

  it('seçim listesi etiketi derinliğe göre girintilenir', () => {
    expect(indentedLabel({ name: 'Giyim', path: 'Giyim' })).toBe('Giyim')
    expect(indentedLabel({ name: 'Tişört', path: 'Giyim > Tişört' })).toBe('— Tişört')
    expect(indentedLabel({ name: 'Erkek', path: 'Giyim > Tişört > Erkek' })).toBe('— — Erkek')
  })

  it('adres etiketi', () => {
    expect(slugLabel(all[1])).toBe('/b')
  })
})
