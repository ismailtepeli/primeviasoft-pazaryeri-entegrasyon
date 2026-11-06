export interface CartLine { productId: string; quantity: number }

/** Sunucudaki sınırlarla aynıdır (CheckoutService); asıl denetim sunucudadır. */
export const MAX_QUANTITY = 99
export const MAX_LINES = 50

const clamp = (n: number) => Math.min(Math.max(Math.trunc(n) || 0, 0), MAX_QUANTITY)

export function addLine(lines: CartLine[], productId: string, quantity = 1): CartLine[] {
  const existing = lines.find((l) => l.productId === productId)
  if (existing) return lines.map((l) => (l === existing ? { ...l, quantity: clamp(l.quantity + quantity) } : l))
  if (lines.length >= MAX_LINES || clamp(quantity) === 0) return lines
  return [...lines, { productId, quantity: clamp(quantity) }]
}

/** Satırın adedini belirler; 0 ya da altı satırı kaldırır. */
export function setQuantity(lines: CartLine[], productId: string, quantity: number): CartLine[] {
  const q = clamp(quantity)
  return q === 0 ? lines.filter((l) => l.productId !== productId) : lines.map((l) => (l.productId === productId ? { ...l, quantity: q } : l))
}

export const removeLine = (lines: CartLine[], productId: string): CartLine[] => lines.filter((l) => l.productId !== productId)

export const cartCount = (lines: CartLine[]): number => lines.reduce((sum, l) => sum + l.quantity, 0)

/** Saklanan sepeti okur; bozuk ya da elle değiştirilmiş veriyi eler (kimlik biçimi, adet sınırı, yinelenen satır, satır sayısı). */
export function parseStored(raw: string | null): CartLine[] {
  if (!raw) return []
  try {
    const data: unknown = JSON.parse(raw)
    if (!Array.isArray(data)) return []
    const seen = new Set<string>()
    const lines: CartLine[] = []
    for (const item of data) {
      const id = (item as CartLine | null)?.productId
      const quantity = clamp(Number((item as CartLine | null)?.quantity))
      if (typeof id !== 'string' || !/^[0-9a-f-]{36}$/i.test(id) || quantity === 0 || seen.has(id)) continue
      seen.add(id)
      lines.push({ productId: id, quantity })
      if (lines.length >= MAX_LINES) break
    }
    return lines
  } catch { return [] }
}

// ---------- sunucu fiyatlaması ----------

export interface QuoteIssue { kind: string; text: string; available: number | null }

export interface QuoteLine {
  productId: string; name: string; coverUrl: string | null; quantity: number; unitPrice: number; listPrice: number | null; lineTotal: number; issue: QuoteIssue | null
}

export interface PaymentOption { method: string; label: string; fee: number; info: string | null }

/** Sepete uygun kargo seçeneği; firma tanımlı olmayan mağazada tek, firmasız (carrierId boş) seçenek gelir. */
export interface ShippingOption { carrierId: string | null; name: string; fee: number }

export interface CartQuote {
  lines: QuoteLine[]; subtotal: number; shippingFee: number; freeShippingRemaining: number | null; total: number; currency: string; hasIssues: boolean; paymentOptions: PaymentOption[]
  shippingOptions: ShippingOption[]; carrierId: string | null
}

/** Müşterinin seçebileceği firma var mı (firma tanımlıysa seçenekler firmalıdır). */
export function hasCarrierChoice(quote: Pick<CartQuote, 'shippingOptions'>): boolean {
  return quote.shippingOptions.some((o) => o.carrierId !== null)
}

export interface CheckoutResult { orderId: string; orderNumber: string; paymentMethod: string; paymentStatus: string; total: number; paymentInfo: string | null; paymentUrl: string | null }

/** Sorunlu satırı düzeltmek için önerilen adet (stoktaki kadar); düzeltilemiyorsa null (satır kaldırılmalı). */
export function fixedQuantity(line: QuoteLine): number | null {
  if (!line.issue) return line.quantity
  return line.issue.kind === 'stock' && (line.issue.available ?? 0) > 0 ? line.issue.available : null
}

/** Seçilen ödeme yönteminin ek ücretiyle görüntülenen toplam (kesin tutar siparişte sunucuda hesaplanır). */
export function totalWith(quote: Pick<CartQuote, 'total' | 'paymentOptions'>, method: string | null): number {
  const fee = quote.paymentOptions.find((o) => o.method === method)?.fee ?? 0
  return Math.round((quote.total + fee) * 100) / 100
}

export const paymentMethodLabel: Record<string, string> = { BankTransfer: 'Havale / EFT', CashOnDelivery: 'Kapıda ödeme', Card: 'Kredi / banka kartı' }

export const paymentStatusLabel: Record<string, string> = {
  Pending: 'Ödeme bekleniyor', Paid: 'Ödendi', OnDelivery: 'Teslimatta ödenecek', Failed: 'Ödeme alınamadı', Expired: 'Ödeme süresi doldu',
}
