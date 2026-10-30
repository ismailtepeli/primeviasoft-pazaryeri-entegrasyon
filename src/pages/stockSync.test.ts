import { describe, expect, it } from 'vitest'
import { pendingText, syncBadge } from './stockSync'

const s = (over: Partial<Parameters<typeof syncBadge>[0]> = {}) => ({ supported: true, syncInFlight: false, pendingChanges: 0, lastFailed: 0, pendingBatches: 0, ...over })

describe('syncBadge', () => {
  it('öncelik sırasıyla durumu seçer', () => {
    expect(syncBadge(s({ supported: false })).text).toBe('Desteklenmiyor')
    expect(syncBadge(s({ syncInFlight: true, lastFailed: 3 })).text).toBe('Gönderiliyor')
    expect(syncBadge(s({ lastFailed: 3, pendingChanges: 2 })).text).toBe('Hata var')
    expect(syncBadge(s({ pendingChanges: 2, pendingBatches: 1 })).text).toBe('Bekleyen değişiklik')
    expect(syncBadge(s({ pendingBatches: 1 })).text).toBe('Sonuç bekleniyor')
    expect(syncBadge(s()).text).toBe('Güncel')
  })
})

describe('pendingText', () => {
  it('bekleyen ve ertelenen barkodları anlatır', () => {
    expect(pendingText({ pendingChanges: 0, deferred: 0 })).toBe('Kanala gönderilmemiş değişiklik yok.')
    expect(pendingText({ pendingChanges: 12, deferred: 0 })).toContain('12 barkodun')
    expect(pendingText({ pendingChanges: 12, deferred: 3 })).toContain('3 barkod dakikalık fiyat sınırı')
  })
})
