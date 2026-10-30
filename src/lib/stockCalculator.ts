export const MAX_STOCK = 10_000_000

/** Ürüne özel güvenlik stoğu varsa o, yoksa kanalın varsayılanı geçerlidir (0 açıkça verilmiş bir değerdir). */
export function effectiveSafety(productSafety: number | null, ruleSafety: number): number {
  return productSafety ?? ruleSafety
}

/** Sunucudaki StockCalculator ile aynı kurallar: (stok − güvenlik) × pay, aşağı yuvarlanır, negatif olmaz. */
export function channelQuantity(physicalStock: number, safetyStock: number, allocationPercent: number): number {
  const sellable = Math.max(0, physicalStock - Math.max(0, safetyStock))
  const percent = Math.min(100, Math.max(0, allocationPercent))
  // Kayan nokta (örn. 899.9999) aşağı yuvarlamayı bir eksik göstermesin diye küçük tolerans.
  return Math.floor(sellable * percent / 100 + 1e-9)
}

/** Kural değerleri geçersizse hata metni; geçerliyse null. */
export function validateStockRule(safetyStock: number, allocationPercent: number): string | null {
  if (!Number.isInteger(safetyStock) || safetyStock < 0 || safetyStock > MAX_STOCK) return 'Güvenlik stoğu 0 ile 10.000.000 arasında bir tam sayı olmalı.'
  if (!(allocationPercent >= 0 && allocationPercent <= 100)) return 'Kanala açılan pay 0 ile 100 arasında olmalı.'
  return null
}
