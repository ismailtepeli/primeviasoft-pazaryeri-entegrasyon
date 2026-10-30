import { describe, expect, it } from 'vitest'
import { describeSend, sendWasBlocked, sendableChannels } from './sendHelpers'

const ch = (channel: string, status: 'Connected' | 'Unverified' | 'AuthError' | 'NotConnected') => ({ channel, status, lastSyncAt: null })
const result = (over: Partial<{ jobs: number; products: number; unknown: number; blocked: number }> = {}) => ({ jobs: 1, products: 3, unknown: 0, blocked: 0, ...over })

describe('sendHelpers', () => {
  it('yalnızca bağlı ve gönderimi desteklenen kanalları seçer', () => {
    const list = [ch('Trendyol', 'Connected'), ch('Hepsiburada', 'Connected'), ch('N11', 'NotConnected'), ch('Amazon', 'Unverified')]
    expect(sendableChannels(list).map((c) => c.channel)).toEqual(['Trendyol'])
    expect(sendableChannels([ch('Trendyol', 'Unverified')])).toEqual([])
  })

  it('sonuç cümlesini kurar', () => {
    expect(describeSend('Trendyol', result())).toBe('3 ürün Trendyol için kuyruğa alındı.')
    expect(describeSend('Trendyol', result({ unknown: 2 }))).toBe('3 ürün Trendyol için kuyruğa alındı, 2 ürün bulunamadı.')
  })

  it('eksik bilgi nedeniyle engellenenleri bildirir', () => {
    expect(describeSend('Trendyol', result({ products: 2, blocked: 1 })))
      .toBe('2 ürün Trendyol için kuyruğa alındı, 1 ürün eksik bilgi nedeniyle gönderilmedi (Ürünler listesinde "Eksik" olarak nedenleriyle görünür).')
    expect(describeSend('Trendyol', result({ jobs: 0, products: 0, blocked: 4 }))).toContain('Hiçbir ürün Trendyol için kuyruğa alınmadı, 4 ürün eksik')
  })

  it('hepsi engellendiyse uyarı olarak ayırt edilir', () => {
    expect(sendWasBlocked(result({ jobs: 0, products: 0, blocked: 2 }))).toBe(true)
    expect(sendWasBlocked(result({ blocked: 2 }))).toBe(false)
    expect(sendWasBlocked(result({ jobs: 0, products: 0, blocked: 0 }))).toBe(false)
  })
})
