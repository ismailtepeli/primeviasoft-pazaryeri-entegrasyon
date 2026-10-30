import type { OrderStatusKind } from '../api/types'

export const orderStepLabels = ['Oluştu', 'Toplanıyor', 'Faturalandı', 'Etiket', 'Kargoda'] as const

export type StepState = 'done' | 'active' | 'todo'

export interface OrderStep {
  label: string
  state: StepState
}

/**
 * Aktif adımın sırası (0–4); tümü tamamlandıysa 5; iptal/iade siparişte null.
 * Etiket adımı için kanaldan sinyal gelmez; kargoya verilince tamamlanmış sayılır.
 */
export function activeStepIndex(status: OrderStatusKind, channelStatus: string): number | null {
  switch (status) {
    case 'New': return 0
    case 'Preparing': return channelStatus.toLowerCase() === 'invoiced' ? 2 : 1
    case 'Shipped': return 4
    case 'Delivered': return 5
    default: return null
  }
}

/** Akış çubuğu: tamamlanan adımlar 'done', geçerli adım 'active', kalanlar 'todo'. */
export function orderSteps(status: OrderStatusKind, channelStatus: string): OrderStep[] {
  const active = activeStepIndex(status, channelStatus)
  return orderStepLabels.map((label, i) => ({
    label,
    state: active === null ? 'todo' : i < active ? 'done' : i === active ? 'active' : 'todo',
  }))
}

/** İptal/iade siparişte akış durur; kullanıcıya gösterilecek not (diğerlerinde null). */
export function stoppedNote(status: OrderStatusKind): string | null {
  return status === 'Cancelled' ? 'Sipariş iptal edildi; akış durdu.' : status === 'Returned' ? 'Sipariş iade edildi; akış durdu.' : null
}
