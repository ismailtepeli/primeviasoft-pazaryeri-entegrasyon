import type { DashboardSummary } from '../api/types'
import { anyModuleEnabled } from '../modules/moduleState'

export interface KpiCardData {
  label: string
  value: string | number
  hint?: string
  to: string
  tone?: 'danger'
  /** Kartın görünmesi için açık olması gereken modüllerden en az biri. */
  anyOf: string[]
}

const number = new Intl.NumberFormat('tr-TR')

/**
 * Genel bakışın dört KPI kartı. Özet gelmediyse değerler "—" olur. Senkron hatası sıfırdan büyükse kart kırmızıdır;
 * her kart ilgili listeye (varsa filtreli) bağlanır.
 */
export function kpiCards(s: DashboardSummary | undefined, enabled: Set<string> | null = null): KpiCardData[] {
  const v = (n: number | undefined) => (n === undefined ? '—' : number.format(n))
  const cards: KpiCardData[] = [
    { label: 'Bugünkü sipariş', value: v(s?.todayOrders), hint: 'Bugün gelen siparişler', to: '/siparisler?tab=new', anyOf: ['orders'] },
    { label: 'Bekleyen kargo', value: v(s?.awaitingShipment), hint: 'Hazırlanıyor, kargoya verilmedi', to: '/siparisler?tab=preparing', anyOf: ['orders'] },
    {
      label: 'Senkron hatası', value: v(s?.syncErrors), to: '/senkron-hatalari', anyOf: ['marketplace', 'orders'],
      hint: s && s.syncErrors > 0 ? 'Gözden geçirin' : 'Hata yok',
      tone: s && s.syncErrors > 0 ? 'danger' : undefined,
    },
    { label: 'Aktif ürün', value: v(s?.activeProducts), hint: 'En az bir kanalda yayında', to: '/urunler', anyOf: ['marketplace'] },
    { label: 'B2B onay bekleyen', value: v(s?.b2bPendingApproval), hint: 'Bayi siparişleri', to: '/siparisler?tab=new&channel=B2B', anyOf: ['b2b-store'], tone: s && s.b2bPendingApproval > 0 ? 'danger' : undefined },
  ]
  return cards.filter((c) => anyModuleEnabled(c.anyOf, enabled))
}
