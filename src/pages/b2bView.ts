import type { StockDisplayKind } from '../api/types'

/** Genel fiyatın bayi sitesinde gösterim seçenekleri (ayar değeri showVatIncluded). */
export const vatDisplayOptions: { value: 'true' | 'false'; label: string }[] = [
  { value: 'true', label: 'KDV dahil' },
  { value: 'false', label: 'KDV hariç' },
]

export const stockDisplayOptions: { value: StockDisplayKind; label: string }[] = [
  { value: 'Level', label: 'Stokta var / Az kaldı / Tükendi' },
  { value: 'Exact', label: 'Kesin adet' },
  { value: 'Hidden', label: 'Gösterme' },
]

/** Boş olmayan, yalnızca rakamdan oluşan metni sayıya çevirir; aksi halde null. */
export function parseCount(text: string): number | null {
  const t = text.trim()
  return /^\d+$/.test(t) ? Number(t) : null
}
