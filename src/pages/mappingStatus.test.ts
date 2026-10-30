import { describe, expect, it } from 'vitest'
import type { CategoryMappingRow, ChannelCategory } from '../api/types'
import { blockingCount, mappingStatus } from './mappingStatus'

const cat = (isActive: boolean): ChannelCategory => ({ externalId: 1, name: 'Tişört', path: 'Giyim > Tişört', isLeaf: true, hasChildren: false, isActive })
const row = (productCount: number, mapped: ChannelCategory | null): CategoryMappingRow => ({ categoryId: crypto.randomUUID(), path: 'X', productCount, mapped })

describe('mappingStatus', () => {
  it('üç durumu ayırır', () => {
    expect(mappingStatus(row(0, null)).text).toBe('Eşlenmedi')
    expect(mappingStatus(row(0, cat(true))).text).toBe('Eşlendi')
    expect(mappingStatus(row(0, cat(false))).text).toBe('Kanalda kaldırılmış')
  })

  it('yalnızca ürünü olan eşlenmemiş/geçersiz kategorileri engelleyici sayar', () => {
    const rows = [row(3, null), row(2, cat(false)), row(0, null), row(5, cat(true))]
    expect(blockingCount(rows)).toBe(2)
  })
})
