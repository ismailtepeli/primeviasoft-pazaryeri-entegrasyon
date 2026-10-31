import type { PriceListInput, PriceListRow, PricePreviewRow } from '../api/types'

export const PRICE_PAGE_SIZE = 20

export const currencies = ['TRY', 'USD', 'EUR'] as const

const symbols: Record<string, string> = { TRY: '₺', USD: '$', EUR: '€' }

export function formatMoney(value: number, currency = 'TRY'): string {
  return `${new Intl.NumberFormat('tr-TR', { minimumFractionDigits: 2, maximumFractionDigits: 2 }).format(value)} ${symbols[currency] ?? currency}`
}

/** "Bayi grubu: Altın" gibi liste bağlamı; hiçbirine bağlı değilse uyarı. */
export function listTarget(l: Pick<PriceListRow, 'groupName' | 'dealerName'>): string {
  if (l.dealerName) return `Bayi: ${l.dealerName}`
  if (l.groupName) return `Grup: ${l.groupName}`
  return 'Bağlı değil'
}

export const priceSourceLabel: Record<PricePreviewRow['source'], string> = {
  DealerList: 'Bayiye özel liste',
  GroupList: 'Grup listesi',
  General: 'Genel fiyat',
}

export type TargetKind = 'none' | 'group' | 'dealer'

export interface PriceListForm {
  name: string
  currency: string
  targetKind: TargetKind
  targetId: string
  discount: string
  pricesIncludeVat: boolean
  showVatIncluded: boolean
  isActive: boolean
}

export const emptyPriceListForm: PriceListForm = {
  name: '', currency: 'TRY', targetKind: 'group', targetId: '', discount: '', pricesIncludeVat: false, showVatIncluded: false, isActive: true,
}

export function formFromList(l: PriceListRow): PriceListForm {
  return {
    name: l.name, currency: l.currency, targetKind: l.dealerId ? 'dealer' : l.groupId ? 'group' : 'none', targetId: l.dealerId ?? l.groupId ?? '',
    discount: l.discountPercent == null ? '' : String(l.discountPercent).replace('.', ','), pricesIncludeVat: l.pricesIncludeVat, showVatIncluded: l.showVatIncluded, isActive: l.isActive,
  }
}

/** Virgüllü ya da noktalı sayı; boş/geçersiz → null. */
export function parseDecimal(text: string): number | null {
  const t = text.trim().replace(',', '.')
  if (t === '' || !/^\d+(\.\d+)?$/.test(t)) return null
  return Number(t)
}

export function parseCount(text: string): number | null {
  const t = text.trim()
  return /^\d+$/.test(t) ? Number(t) : null
}

/** Alan adı → hata mesajı; sunucu aynı kuralları yeniden doğrular. */
export function validatePriceListForm(f: PriceListForm): Record<string, string> {
  const errors: Record<string, string> = {}
  if (!f.name.trim()) errors.name = 'Liste adı boş olamaz.'
  else if (f.name.trim().length > 120) errors.name = 'Liste adı 120 karakteri aşamaz.'
  if (f.targetKind !== 'none' && !f.targetId) errors.targetId = f.targetKind === 'group' ? 'Bir bayi grubu seçin.' : 'Bir bayi seçin.'
  if (f.discount.trim() !== '') {
    const d = parseDecimal(f.discount)
    if (d == null || d > 100) errors.discount = 'İskonto 0 ile 100 arasında bir sayı olmalı.'
  }
  return errors
}

export function toPriceListInput(f: PriceListForm): PriceListInput {
  return {
    name: f.name.trim(), currency: f.currency,
    groupId: f.targetKind === 'group' ? f.targetId : null, dealerId: f.targetKind === 'dealer' ? f.targetId : null,
    discountPercent: parseDecimal(f.discount), pricesIncludeVat: f.pricesIncludeVat, showVatIncluded: f.showVatIncluded, isActive: f.isActive,
  }
}

/** Ürün adet ayarı için doğrulama; boş bırakılan alan 1 sayılmaz, hata verilir. */
export function validateQuantity(min: string, pack: string): string | null {
  const m = parseCount(min), p = parseCount(pack)
  if (m == null || p == null || m < 1 || p < 1) return 'Minimum ve koli adedi en az 1 olan tam sayı olmalı.'
  if (m > 100000 || p > 100000) return 'Adetler 100000\'i aşamaz.'
  return null
}

export function priceImportSummary(r: { created: number; updated: number; skipped: number }): string {
  const parts = [`${r.created} yeni`, `${r.updated} güncellendi`]
  if (r.skipped > 0) parts.push(`${r.skipped} satır atlandı`)
  return parts.join(', ') + '.'
}

/** Dışa aktarılan Excel'in dosya adı: liste adından güvenli, küçük harfli bir ad (sunucudakiyle aynı kural). */
export function exportFileName(name?: string): string {
  const safe = (name ?? '').toLowerCase().replace(/[^a-z0-9ığüşöç]+/g, '-').replace(/^-+|-+$/g, '')
  return `fiyat-listesi-${safe || 'liste'}.xlsx`
}
