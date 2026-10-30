import { describe, expect, it } from 'vitest'
import type { DashboardSummary } from '../api/types'
import { kpiCards } from './kpiCards'

const summary = (over: Partial<DashboardSummary> = {}): DashboardSummary => ({ todayOrders: 0, awaitingShipment: 0, syncErrors: 0, activeProducts: 0, b2bPendingApproval: 0, ...over })

describe('kpiCards', () => {
  it('kartları sırayla verir ve sayıları Türkçe biçimler', () => {
    const cards = kpiCards(summary({ todayOrders: 1280, awaitingShipment: 4, activeProducts: 4310, b2bPendingApproval: 2 }))
    expect(cards.map((c) => `${c.label}:${c.value}`)).toEqual(['Bugünkü sipariş:1.280', 'Bekleyen kargo:4', 'Senkron hatası:0', 'Aktif ürün:4.310', 'B2B onay bekleyen:2'])
  })

  it('senkron hatası sıfırdan büyükse kart kırmızıdır, sıfırsa değildir', () => {
    expect(kpiCards(summary({ syncErrors: 3 }))[2].tone).toBe('danger')
    expect(kpiCards(summary())[2].tone).toBeUndefined()
  })

  it('B2B onay bekleyen kart sıfırdan büyükse kırmızıdır ve bekleyen B2B siparişlerine bağlanır', () => {
    const card = (n: number) => kpiCards(summary({ b2bPendingApproval: n })).find((c) => c.label === 'B2B onay bekleyen')!
    expect(card(3).tone).toBe('danger')
    expect(card(0).tone).toBeUndefined()
    expect(card(1).to).toBe('/siparisler?tab=new&channel=B2B')
  })

  it('kartlar ilgili listeye filtreli bağlanır', () => {
    expect(kpiCards(undefined).map((c) => c.to)).toEqual(['/siparisler?tab=new', '/siparisler?tab=preparing', '/senkron-hatalari', '/urunler', '/siparisler?tab=new&channel=B2B'])
  })

  it('kapalı modülün kartlarını göstermez', () => {
    const labels = (on: string[]) => kpiCards(undefined, new Set(on)).map((c) => c.label)
    expect(labels(['core', 'marketplace'])).toEqual(['Senkron hatası', 'Aktif ürün'])
    expect(labels(['core', 'orders'])).toEqual(['Bugünkü sipariş', 'Bekleyen kargo', 'Senkron hatası'])
    expect(labels(['core', 'orders', 'b2b-store'])).toEqual(['Bugünkü sipariş', 'Bekleyen kargo', 'Senkron hatası', 'B2B onay bekleyen'])
    expect(labels(['core'])).toEqual([])
  })

  it('özet gelmediyse değer yerine çizgi gösterir', () => {
    expect(kpiCards(undefined).map((c) => c.value)).toEqual(['—', '—', '—', '—', '—'])
  })
})
