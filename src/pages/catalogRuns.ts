import type { CatalogRun } from '../api/types'

export const kindLabel: Record<CatalogRun['kind'], string> = {
  Categories: 'Kategoriler',
  Brands: 'Markalar',
  Attributes: 'Özellikler',
}

/** Bir senkronun değişiklik özeti; hata varsa hata, değişiklik yoksa "değişiklik yok". */
export function runSummary(r: Pick<CatalogRun, 'added' | 'changed' | 'removed' | 'error'>): string {
  if (r.error) return `Hata: ${r.error}`
  const parts = [
    r.added > 0 ? `${r.added} yeni` : '',
    r.changed > 0 ? `${r.changed} değişti` : '',
    r.removed > 0 ? `${r.removed} kaldırıldı` : '',
  ].filter(Boolean)
  return parts.length > 0 ? parts.join(', ') : 'Değişiklik yok'
}
