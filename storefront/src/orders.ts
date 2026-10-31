import type { StorePrice, StoreStock } from './catalog'

export interface CartIssue { kind: 'unavailable' | 'stock' | 'quantity' | 'currency'; text: string }

export interface CartLine {
  productId: string
  name: string
  code: string
  coverUrl: string | null
  quantity: number
  price: StorePrice
  lineTotal: number
  minOrderQuantity: number
  packSize: number
  stock: StoreStock
  issue: CartIssue | null
  priceAtAdd: number | null
}

export interface Cart { lines: CartLine[]; totalNet: number; totalGross: number; currency: string; hasIssues: boolean; itemCount: number }

export interface OrderRow { id: string; orderNumber: string; orderDate: string; status: string; totalGross: number; currency: string; lineCount: number }

export interface OrderLine { productId: string | null; name: string; code: string; quantity: number; unitPrice: number; priceIncludesVat: boolean; vatRate: number }

export interface OrderDetail {
  id: string
  orderNumber: string
  orderDate: string
  status: string
  totalNet: number
  totalGross: number
  currency: string
  shippingAddress: string | null
  note: string | null
  canCancel: boolean
  lines: OrderLine[]
  shipmentCarrier: string | null
  trackingNumber: string | null
  shippedAt: string | null
  /** Ret/iptal gerekçesi. */
  statusNote: string | null
}

/** Sepet sayacı: koli adedi olan ürünler koli, diğerleri adet sayılır (ürün kartındaki adet kutusuyla aynı birim). */
export function cartUnits(lines: Pick<CartLine, 'quantity' | 'packSize'>[]): number {
  return lines.reduce((sum, l) => sum + (l.packSize > 1 && l.quantity % l.packSize === 0 ? l.quantity / l.packSize : l.quantity), 0)
}

/** "2 koli · 24 adet" ya da yalnızca koli yoksa "5 adet". */
export function cartSummary(lines: Pick<CartLine, 'quantity' | 'packSize'>[]): string {
  const pieces = lines.reduce((s, l) => s + l.quantity, 0)
  const boxes = lines.reduce((s, l) => s + (l.packSize > 1 && l.quantity % l.packSize === 0 ? l.quantity / l.packSize : 0), 0)
  return boxes > 0 ? `${boxes} koli · ${pieces} adet` : `${pieces} adet`
}

export const emptyCart: Cart = { lines: [], totalNet: 0, totalGross: 0, currency: 'TRY', hasIssues: false, itemCount: 0 }

const statusText: Record<string, string> = {
  New: 'Onay bekliyor', Preparing: 'Onaylandı · hazırlanıyor', Shipped: 'Sevk edildi', Delivered: 'Teslim edildi', Cancelled: 'İptal edildi', Returned: 'İade edildi',
}

export const orderStatusText = (status: string): string => statusText[status] ?? status

export interface Step { label: string; state: 'done' | 'current' | 'todo' }

const FLOW = ['New', 'Preparing', 'Shipped', 'Delivered'] as const

/** Bayiye gösterilen durum çubuğu; iptal/iade akış dışı olduğu için tek adımdır. */
export function orderSteps(status: string): Step[] {
  if (status === 'Cancelled' || status === 'Returned') return [{ label: orderStatusText(status), state: 'current' }]
  const at = Math.max(0, FLOW.indexOf(status as (typeof FLOW)[number]))
  return FLOW.map((s, i) => ({ label: orderStatusText(s), state: i < at ? 'done' : i === at ? 'current' : 'todo' }))
}

/** Sepette fiyat, bayi sepete eklediğinden farklıysa uyarı metni; aynıysa null. */
export function priceChangeNote(line: CartLine): string | null {
  if (line.priceAtAdd == null || line.issue?.kind === 'unavailable') return null
  const diff = Math.round((line.price.amount - line.priceAtAdd) * 100) / 100
  if (diff === 0) return null
  return diff > 0 ? 'Fiyat arttı' : 'Fiyat düştü'
}

export function formatDate(iso: string): string {
  return new Date(iso).toLocaleString('tr-TR', { dateStyle: 'medium', timeStyle: 'short' })
}

/**
 * Sipariş formunda adres denetimi: bayinin kayıtlı adresi yoksa (null) adres girilmeden sipariş verilemez.
 * saved undefined ise (bilgi yüklenemedi) sunucu karar verir.
 */
export function addressRequired(saved: string | null | undefined, typed: string): string | null {
  return saved === null && !typed.trim() ? 'Kayıtlı adresiniz yok; teslimat adresini girin.' : null
}
