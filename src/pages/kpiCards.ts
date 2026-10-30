import type { DashboardSummary } from '../api/types'

export interface KpiCardData {
  label: string
  value: string | number
  hint?: string
  to: string
  tone?: 'danger'
}

const number = new Intl.NumberFormat('tr-TR')

/**
 * Genel bakışın dört KPI kartı. Özet gelmediyse değerler "—" olur. Senkron hatası sıfırdan büyükse kart kırmızıdır;
 * her kart ilgili listeye (varsa filtreli) bağlanır.
 */
export function kpiCards(s: DashboardSummary | undefined): KpiCardData[] {
  const v = (n: number | undefined) => (n === undefined ? '—' : number.format(n))
  return [
    { label: 'Bugünkü sipariş', value: v(s?.todayOrders), hint: 'Bugün gelen siparişler', to: '/siparisler?tab=new' },
    { label: 'Bekleyen kargo', value: v(s?.awaitingShipment), hint: 'Hazırlanıyor, kargoya verilmedi', to: '/siparisler?tab=preparing' },
    {
      label: 'Senkron hatası', value: v(s?.syncErrors), to: '/senkron-hatalari',
      hint: s && s.syncErrors > 0 ? 'Gözden geçirin' : 'Hata yok',
      tone: s && s.syncErrors > 0 ? 'danger' : undefined,
    },
    { label: 'Aktif ürün', value: v(s?.activeProducts), hint: 'En az bir kanalda yayında', to: '/urunler' },
  ]
}
