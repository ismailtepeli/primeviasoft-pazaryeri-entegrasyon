export type PriceMode = 'percent' | 'amount' | 'set'

export const PRICE_MAX = 9_999_999_999

export const modeLabel: Record<PriceMode, string> = {
  percent: 'Yüzde (%) artır / azalt',
  amount: 'Tutar (₺) ekle / çıkar',
  set: 'Sabit fiyat yap',
}

/** "12,5", "1.250,50", "12.50" gibi yazılmış fiyatı sayıya çevirir; sayı değilse null. Son ayırıcı ondalık sayılır. */
export function parsePriceText(text: string): number | null {
  const t = text.trim().replace(/\s|₺|TL/gi, '')
  if (!t || !/^-?[\d.,]+$/.test(t)) return null
  const normalized = t.lastIndexOf(',') > t.lastIndexOf('.') ? t.replace(/\./g, '').replace(',', '.') : t.replace(/,/g, '')
  const n = Number(normalized)
  return Number.isFinite(n) ? n : null
}

const round2 = (v: number) => Math.round((v + Number.EPSILON) * 100) / 100

/** Tek ürün için yeni fiyat; geçersizse hata metni. */
export function validateSinglePrice(text: string): { price: number } | { error: string } {
  const n = parsePriceText(text)
  if (n === null) return { error: 'Fiyat sayı olmalı.' }
  const price = round2(n)
  if (price <= 0) return { error: 'Fiyat sıfırdan büyük olmalı.' }
  if (price > PRICE_MAX) return { error: 'Fiyat çok büyük.' }
  return { price }
}

/** Sunucudaki kuralla aynı: yeni fiyat; sıfır/negatif/çok büyükse null. */
export function applyBulk(mode: PriceMode, value: number, current: number): number | null {
  const next = mode === 'percent' ? round2(current * (1 + value / 100)) : mode === 'amount' ? round2(current + value) : round2(value)
  return next > 0 && next <= PRICE_MAX ? next : null
}

/** Toplu form girdisi; geçerliyse sayısal değeri, değilse hata metnini döner. */
export function validateBulk(mode: PriceMode, text: string): { value: number } | { error: string } {
  const n = parsePriceText(text)
  if (n === null) return { error: 'Bir sayı girin.' }
  if (mode === 'percent' && (n < -90 || n > 1000)) return { error: 'Yüzde -90 ile 1000 arasında olmalı.' }
  if (mode === 'set' && (n <= 0 || n > PRICE_MAX)) return { error: 'Fiyat sıfırdan büyük olmalı.' }
  if (mode === 'amount' && Math.abs(n) > PRICE_MAX) return { error: 'Tutar çok büyük.' }
  return { value: n }
}

export function bulkSummary(r: { updated: number; unchanged: number; skipped: unknown[] }): string {
  const parts = [`${r.updated} ürünün fiyatı güncellendi`]
  if (r.unchanged > 0) parts.push(`${r.unchanged} ürün zaten aynı fiyattaydı`)
  if (r.skipped.length > 0) parts.push(`${r.skipped.length} ürün atlandı (yeni fiyat sıfırdan büyük olmalı)`)
  return parts.join(', ') + '.'
}

export function priceImportSummary(r: { updated: number; unchanged: number; errors: unknown[] }): string {
  const parts = [`${r.updated} ürünün fiyatı güncellendi`, `${r.unchanged} ürün zaten güncel`]
  if (r.errors.length > 0) parts.push(`${r.errors.length} satır hatalı`)
  return parts.join(', ') + '.'
}
