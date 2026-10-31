import type { ProductImportError } from '../api/types'

const cell = (v: string | number) => `"${String(v).replace(/"/g, '""')}"`

/** Hata raporu CSV'si; Excel'in Türkçe karakterleri doğru açması için başına BOM konur. */
export function errorsToCsv(errors: ProductImportError[]): string {
  const lines = [['Satır', 'Barkod', 'Hata'].map(cell).join(';')]
  for (const e of errors) lines.push([e.row, e.barcode, e.message].map(cell).join(';'))
  return '﻿' + lines.join('\r\n')
}

/** Yükleme sonucunun tek cümlelik özeti. */
export function importSummary(r: { created: number; updated: number; groups: number; errors: unknown[] }): string {
  // Hiç yeni ürün yoksa kullanıcı "yüklenmedi" sanmasın: barkodu zaten kayıtlı ürünlerin güncellendiği açıkça söylenir.
  if (r.created === 0 && r.updated > 0 && r.errors.length === 0)
    return `Yeni ürün eklenmedi: dosyadaki ${r.updated} ürünün barkodu zaten kayıtlıydı, bu ürünler güncellendi. Yeni ürün eklemek için farklı barkodlar kullanın.`
  const parts = [`${r.created} yeni ürün eklendi`, `${r.updated} ürün güncellendi`]
  if (r.groups > 0 && r.created + r.updated > r.groups) parts.push(`${r.groups} ürün grubunda toplandı`)
  if (r.errors.length > 0) parts.push(`${r.errors.length} satır hatalı`)
  return parts.join(', ') + '.'
}
