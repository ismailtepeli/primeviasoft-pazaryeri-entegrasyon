import { describe, expect, it } from 'vitest'
import { describeSend, sendableChannels } from './sendHelpers'

const ch = (channel: string, status: 'Connected' | 'Unverified' | 'AuthError' | 'NotConnected') => ({ channel, status, lastSyncAt: null })

describe('sendHelpers', () => {
  it('yalnızca bağlı ve gönderimi desteklenen kanalları seçer', () => {
    const list = [ch('Trendyol', 'Connected'), ch('Hepsiburada', 'Connected'), ch('N11', 'NotConnected'), ch('Amazon', 'Unverified')]
    expect(sendableChannels(list).map((c) => c.channel)).toEqual(['Trendyol'])
    expect(sendableChannels([ch('Trendyol', 'Unverified')])).toEqual([])
  })

  it('sonuç cümlesini kurar', () => {
    expect(describeSend('Trendyol', { jobs: 1, products: 3, unknown: 0 })).toBe('3 ürün Trendyol için kuyruğa alındı.')
    expect(describeSend('Trendyol', { jobs: 1, products: 3, unknown: 2 })).toBe('3 ürün Trendyol için kuyruğa alındı, 2 ürün bulunamadı.')
  })
})
