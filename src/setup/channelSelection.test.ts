import { describe, expect, it } from 'vitest'
import type { SalesChannel } from '../api/types'
import { channelsBeingClosed, closingNote, initialSelection, sameSelection, toggleChannel } from './channelSelection'

const ch = (id: string, enabled: boolean, available = true): SalesChannel => ({ id, name: id.toUpperCase(), description: '', available, enabled, modules: [] })
const channels = [ch('marketplace', true), ch('b2b', false), ch('b2c', false, false)]

describe('channelSelection', () => {
  it('başlangıç seçimi açık ve hazır kanallardır', () => {
    expect([...initialSelection(channels)]).toEqual(['marketplace'])
    expect([...initialSelection([ch('b2c', true, false)])]).toEqual([]) // hazır olmayan kanal seçilmez
  })

  it('seçim değiştirilir ve eskisini bozmaz', () => {
    const start = initialSelection(channels)
    const next = toggleChannel(start, 'b2b')
    expect([...next].sort()).toEqual(['b2b', 'marketplace'])
    expect([...start]).toEqual(['marketplace'])
    expect([...toggleChannel(next, 'marketplace')]).toEqual(['b2b'])
  })

  it('değişiklik var mı karşılaştırır', () => {
    expect(sameSelection(new Set(['a', 'b']), new Set(['b', 'a']))).toBe(true)
    expect(sameSelection(new Set(['a']), new Set(['a', 'b']))).toBe(false)
  })

  it('kapanacak kanalları bulur ve uyarı metnini kurar', () => {
    const closing = channelsBeingClosed(channels, new Set(['b2b']))
    expect(closing.map((c) => c.id)).toEqual(['marketplace'])
    expect(closingNote(closing)).toContain('MARKETPLACE kapatılacak')
    expect(channelsBeingClosed(channels, new Set(['marketplace']))).toEqual([])
  })
})
