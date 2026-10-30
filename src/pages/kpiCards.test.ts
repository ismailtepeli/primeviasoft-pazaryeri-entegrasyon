import { describe, expect, it } from 'vitest'
import { kpiCards } from './kpiCards'

describe('kpiCards', () => {
  it('dört kartı sırayla verir ve sayıları Türkçe biçimler', () => {
    const cards = kpiCards({ todayOrders: 1280, awaitingShipment: 4, syncErrors: 0, activeProducts: 4310 })
    expect(cards.map((c) => `${c.label}:${c.value}`)).toEqual(['Bugünkü sipariş:1.280', 'Bekleyen kargo:4', 'Senkron hatası:0', 'Aktif ürün:4.310'])
  })

  it('senkron hatası sıfırdan büyükse kart kırmızıdır, sıfırsa değildir', () => {
    expect(kpiCards({ todayOrders: 0, awaitingShipment: 0, syncErrors: 3, activeProducts: 0 })[2].tone).toBe('danger')
    expect(kpiCards({ todayOrders: 0, awaitingShipment: 0, syncErrors: 0, activeProducts: 0 })[2].tone).toBeUndefined()
  })

  it('kartlar ilgili listeye filtreli bağlanır', () => {
    expect(kpiCards(undefined).map((c) => c.to)).toEqual(['/siparisler?tab=new', '/siparisler?tab=preparing', '/senkron-hatalari', '/urunler'])
  })

  it('kapalı modülün kartlarını göstermez', () => {
    const labels = (on: string[]) => kpiCards(undefined, new Set(on)).map((c) => c.label)
    expect(labels(['core', 'marketplace'])).toEqual(['Senkron hatası', 'Aktif ürün'])
    expect(labels(['core', 'orders'])).toEqual(['Bugünkü sipariş', 'Bekleyen kargo', 'Senkron hatası'])
    expect(labels(['core'])).toEqual([])
  })

  it('özet gelmediyse değer yerine çizgi gösterir', () => {
    expect(kpiCards(undefined).map((c) => c.value)).toEqual(['—', '—', '—', '—'])
  })
})
