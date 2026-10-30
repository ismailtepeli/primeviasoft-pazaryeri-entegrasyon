import type { B2BAction, B2BOrderStatusBadge } from '../api/types'

/** B2B siparişinde durum adları: "Yeni" burada onay bekleyen siparişi, "Hazırlanıyor" onaylanmış siparişi anlatır. */
export const b2bStatusBadge: Record<string, B2BOrderStatusBadge> = {
  New: { text: 'Onay bekliyor', variant: 'warning' },
  Preparing: { text: 'Onaylandı · hazırlanıyor', variant: 'info' },
  Shipped: { text: 'Sevk edildi', variant: 'info' },
  Delivered: { text: 'Teslim edildi', variant: 'success' },
  Cancelled: { text: 'İptal / ret', variant: 'error' },
  Returned: { text: 'İade', variant: 'warning' },
}

export const b2bActionLabel: Record<B2BAction, string> = {
  Approve: 'Onayla',
  Reject: 'Reddet',
  Ship: 'Sevk et',
  Deliver: 'Teslim edildi',
  Cancel: 'İptal et',
}

/** Birincil (tek amber) eylem: durumdaki ilerletici adım; yoksa null. */
export function primaryAction(actions: B2BAction[]): B2BAction | null {
  return (['Approve', 'Ship', 'Deliver'] as const).find((a) => actions.includes(a)) ?? null
}

export function validateShipment(carrier: string, tracking: string): string | null {
  if (!carrier.trim()) return 'Kargo firması zorunlu.'
  if (carrier.trim().length > 100) return 'Kargo firması en fazla 100 karakter olabilir.'
  if (!tracking.trim()) return 'Takip numarası zorunlu.'
  if (tracking.trim().length > 100) return 'Takip numarası en fazla 100 karakter olabilir.'
  return null
}

export function validateReason(reason: string, required: boolean): string | null {
  if (required && !reason.trim()) return 'Gerekçe zorunlu.'
  if (reason.trim().length > 500) return 'Gerekçe en fazla 500 karakter olabilir.'
  return null
}

/** Cari bakiye metni: pozitif borç, negatif alacak. */
export function balanceLabel(balance: number): string {
  if (balance > 0) return 'Bayi borçlu'
  if (balance < 0) return 'Bayi alacaklı'
  return 'Bakiye sıfır'
}

export function validatePaymentForm(amount: string): string | null {
  const n = Number(amount.replace(',', '.'))
  if (!amount.trim() || !Number.isFinite(n) || n <= 0) return 'Geçerli bir tutar girin.'
  return null
}

export const money = new Intl.NumberFormat('tr-TR', { style: 'currency', currency: 'TRY' })

/** Sipariş listesindeki "Sıradaki adım" sütunu (B2B akışı). */
export function b2bNextStep(status: string): string {
  switch (status) {
    case 'New': return 'Onayla ya da reddet'
    case 'Preparing': return 'Sevk et'
    case 'Shipped': return 'Teslimat bekleniyor'
    default: return '—'
  }
}
