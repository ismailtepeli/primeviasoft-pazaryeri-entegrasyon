import type { CategoryMappingRow } from '../api/types'
import type { BadgeVariant } from '../components'

export interface MappingStatus {
  text: string
  variant: BadgeVariant
}

/** Satırın eşleme durumu: Eşlendi / Eşlenmedi / kanaldan kaldırılmış. */
export function mappingStatus(row: Pick<CategoryMappingRow, 'mapped'>): MappingStatus {
  if (!row.mapped) return { text: 'Eşlenmedi', variant: 'warning' }
  if (!row.mapped.isActive) return { text: 'Kanalda kaldırılmış', variant: 'error' }
  return { text: 'Eşlendi', variant: 'success' }
}

/** Ürünleri olduğu halde eşlenmemiş (ya da geçersiz) kategori sayısı: gönderimi engelleyenler. */
export function blockingCount(rows: CategoryMappingRow[]): number {
  return rows.filter((r) => r.productCount > 0 && (!r.mapped || !r.mapped.isActive)).length
}
