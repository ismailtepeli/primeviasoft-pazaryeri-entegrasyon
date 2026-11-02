import { PRICE_MAX, parsePriceText } from './priceEdit'

export type SaleMode = 'percent' | 'price' | 'clear'

export const saleModeLabel: Record<SaleMode, string> = {
  percent: 'Normal fiyattan yüzde indirim',
  price: 'Sabit kampanya fiyatı',
  clear: 'Kampanyayı kaldır',
}

const round2 = (v: number) => Math.round((v + Number.EPSILON) * 100) / 100

/** Sunucudaki kuralla aynı: kampanya fiyatı; sıfır/negatif ya da normal fiyata eşit/yüksekse null (ürün atlanır). */
export function salePriceFor(mode: SaleMode, value: number, normal: number): number | null {
  if (mode === 'clear') return null
  const price = mode === 'percent' ? round2(normal * (1 - value / 100)) : round2(value)
  return price > 0 && price < normal ? price : null
}

/** Form girdisi; "clear" değer istemez. Geçerliyse sayısal değeri, değilse hata metnini döner. */
export function validateSale(mode: SaleMode, text: string): { value: number } | { error: string } {
  if (mode === 'clear') return { value: 0 }
  const n = parsePriceText(text)
  if (n === null) return { error: 'Bir sayı girin.' }
  if (mode === 'percent' && (n < 1 || n > 90)) return { error: 'İndirim yüzdesi 1 ile 90 arasında olmalı.' }
  if (mode === 'price' && (n <= 0 || n > PRICE_MAX)) return { error: 'Kampanya fiyatı sıfırdan büyük olmalı.' }
  return { value: n }
}

export function saleSummary(r: { updated: number; cleared: number; skipped: string[] }): string {
  const parts: string[] = []
  if (r.updated > 0) parts.push(`${r.updated} ürüne kampanya uygulandı`)
  if (r.cleared > 0) parts.push(`${r.cleared} ürünün kampanyası kaldırıldı`)
  if (r.skipped.length > 0) parts.push(`${r.skipped.length} ürün atlandı (kampanya fiyatı normal fiyattan düşük olmalı)`)
  return parts.length ? parts.join(', ') + '.' : 'Değişen ürün olmadı.'
}
