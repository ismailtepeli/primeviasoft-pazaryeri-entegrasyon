import type { OrderCounts, OrderStatusKind } from '../api/types'
import type { BadgeVariant, TabItem } from '../components'

export const ORDER_PAGE_SIZE = 20

export type OrderTab = 'new' | 'preparing' | 'shipped' | 'delivered' | 'cancelled'

const tabValues: OrderTab[] = ['new', 'preparing', 'shipped', 'delivered', 'cancelled']

/** Adres çubuğundaki ?tab= değeri geçerliyse o sekme, değilse 'new' (KPI kartlarından filtreli geçiş). */
export function initialTab(value: string | null): OrderTab {
  return tabValues.find((t) => t === value) ?? 'new'
}

export const emptyCounts: OrderCounts = { new: 0, preparing: 0, shipped: 0, delivered: 0, cancelled: 0 }

/** Sayaçlı sekmeler: Yeni, Hazırlanıyor, Kargoda, Teslim edildi, İptal / İade. */
export function orderTabs(counts: OrderCounts): (TabItem & { value: OrderTab })[] {
  return [
    { value: 'new', label: 'Yeni', count: counts.new },
    { value: 'preparing', label: 'Hazırlanıyor', count: counts.preparing },
    { value: 'shipped', label: 'Kargoda', count: counts.shipped },
    { value: 'delivered', label: 'Teslim edildi', count: counts.delivered },
    { value: 'cancelled', label: 'İptal / İade', count: counts.cancelled },
  ]
}

export const orderStatusBadge: Record<OrderStatusKind, { text: string; variant: BadgeVariant }> = {
  New: { text: 'Yeni', variant: 'info' },
  Preparing: { text: 'Hazırlanıyor', variant: 'warning' },
  Shipped: { text: 'Kargoda', variant: 'info' },
  Delivered: { text: 'Teslim edildi', variant: 'success' },
  Cancelled: { text: 'İptal', variant: 'error' },
  Returned: { text: 'İade', variant: 'warning' },
}

/** Sıradaki adım; tamamlanmış ya da kapanmış siparişte yoktur. */
export function nextStep(status: OrderStatusKind): string {
  switch (status) {
    case 'New': return 'Toplamaya al'
    case 'Preparing': return 'Faturala ve etiket yazdır'
    case 'Shipped': return 'Teslimat bekleniyor'
    default: return '—'
  }
}

export function ordersQuery(tab: OrderTab, search: string, page: number): string {
  const qs = new URLSearchParams({ tab, page: String(page), pageSize: String(ORDER_PAGE_SIZE) })
  if (search.trim()) qs.set('q', search.trim())
  return qs.toString()
}
