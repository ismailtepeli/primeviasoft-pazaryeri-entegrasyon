import type { OrderCounts, OrderStatusKind } from '../api/types'
import type { BadgeVariant, TabItem } from '../components'

export const ORDER_PAGE_SIZE = 20

export type OrderTab = 'all' | 'new' | 'preparing' | 'shipped' | 'delivered' | 'cancelled'

const tabValues: OrderTab[] = ['all', 'new', 'preparing', 'shipped', 'delivered', 'cancelled']

/** Adres çubuğundaki ?tab= değeri geçerliyse o sekme, değilse 'new' (KPI kartlarından filtreli geçiş). */
export function initialTab(value: string | null): OrderTab {
  return tabValues.find((t) => t === value) ?? 'new'
}

export const emptyCounts: OrderCounts = { new: 0, preparing: 0, shipped: 0, delivered: 0, cancelled: 0 }

/** Sayaçlı sekmeler: Yeni, Hazırlanıyor, Kargoda, Teslim edildi, İptal / İade. Dönem süzgeci varken başa, tüm durumları toplayan "Tümü" sekmesi eklenir. */
export function orderTabs(counts: OrderCounts, withAll = false): (TabItem & { value: OrderTab })[] {
  const all = counts.new + counts.preparing + counts.shipped + counts.delivered + counts.cancelled
  return [
    ...(withAll ? [{ value: 'all' as const, label: 'Tümü', count: all }] : []),
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

export function ordersQuery(tab: OrderTab, search: string, page: number, channel = '', period = ''): string {
  const qs = new URLSearchParams({ tab, page: String(page), pageSize: String(ORDER_PAGE_SIZE) })
  if (search.trim()) qs.set('q', search.trim())
  if (channel) qs.set('channel', channel)
  if (period) qs.set('period', period)
  return qs.toString()
}

export type FulfillmentKind = 'picking' | 'invoice'

/**
 * Pazaryeri siparişinde kanala bildirilebilecek sıradaki adım: Created → hazırlanıyor, Picking → faturalandı.
 * Kargoya verildi/teslim kanaldan gelir; B2B siparişleri kendi panelinden yönetilir.
 */
export function fulfillmentAction(channel: string, channelStatus: string): { kind: FulfillmentKind; label: string } | null {
  if (channel === 'B2B') return null
  switch (channelStatus.trim().toLowerCase()) {
    case 'created': return { kind: 'picking', label: 'Toplamaya al' }
    case 'picking': return { kind: 'invoice', label: 'Faturalandı olarak bildir' }
    default: return null
  }
}

export const MAX_INVOICE_NUMBER_LENGTH = 64

/** Fatura numarası boş ya da çok uzunsa hata metni, geçerliyse undefined. */
export function invoiceNumberError(value: string): string | undefined {
  const v = value.trim()
  if (!v) return 'Fatura numarası zorunlu.'
  if (v.length > MAX_INVOICE_NUMBER_LENGTH) return `En fazla ${MAX_INVOICE_NUMBER_LENGTH} karakter olabilir.`
  return undefined
}
