import type { AttributeRow, AttributeStatus } from '../api/types'
import type { BadgeVariant } from '../components'

export const attributeStatus: Record<AttributeStatus, { text: string; variant: BadgeVariant }> = {
  Mapped: { text: 'Eşlendi', variant: 'success' },
  Missing: { text: 'Eksik', variant: 'error' },
  Optional: { text: 'İsteğe bağlı', variant: 'inactive' },
}

/** Satırın nasıl doldurulduğunu anlatan kısa metin. */
export function mappingSummary(row: Pick<AttributeRow, 'mapping'>): string {
  const m = row.mapping
  if (!m) return '—'
  if (m.fixedValueId !== null) return `Sabit: ${m.fixedValueName ?? m.fixedValueId}`
  if (m.fixedCustomValue) return `Sabit: ${m.fixedCustomValue}`
  if (m.sourceAttribute) return `Ürünün "${m.sourceAttribute}" özelliğinden (${m.values.length} değer eşli)`
  return '—'
}

export interface EditorState {
  mode: 'fixed' | 'source'
  fixedValueId: number | null
  fixedCustomValue: string
  sourceAttribute: string
  /** Ürün değeri → seçilen eşleme. */
  values: Record<string, { externalValueId: number | null; customValue: string }>
}

/** Mevcut eşleme ile ürünlerde gördüğümüz değerleri birleştirerek düzenleyiciyi başlatır. */
export function initialEditor(row: AttributeRow, sourceValues: (name: string) => string[]): EditorState {
  const m = row.mapping
  const source = m?.sourceAttribute ?? ''
  const values: EditorState['values'] = {}
  for (const v of sourceValues(source)) values[v] = { externalValueId: null, customValue: '' }
  for (const v of m?.values ?? []) values[v.sourceValue] = { externalValueId: v.externalValueId, customValue: v.customValue ?? '' }
  return {
    mode: m?.sourceAttribute ? 'source' : 'fixed',
    fixedValueId: m?.fixedValueId ?? null,
    fixedCustomValue: m?.fixedCustomValue ?? '',
    sourceAttribute: source,
    values,
  }
}

export interface SaveBody {
  channel: string
  categoryId: string
  externalAttributeId: number
  sourceAttribute: string | null
  fixedValueId: number | null
  fixedCustomValue: string | null
  values: { sourceValue: string; externalValueId: number | null; customValue: string | null }[]
}

/** Düzenleyici durumundan sunucuya gidecek gövde; geçersizse hata metni döner. */
export function buildSaveBody(channel: string, categoryId: string, row: AttributeRow, s: EditorState): SaveBody | string {
  const base = { channel, categoryId, externalAttributeId: row.attributeId }
  if (s.mode === 'fixed') {
    if (s.fixedValueId === null && !s.fixedCustomValue.trim()) return 'Bir değer seçin ya da serbest metin girin.'
    return { ...base, sourceAttribute: null, fixedValueId: s.fixedValueId, fixedCustomValue: s.fixedValueId === null ? s.fixedCustomValue.trim() : null, values: [] }
  }
  if (!s.sourceAttribute.trim()) return 'Ürünün hangi özelliğinden doldurulacağını seçin.'
  const values = Object.entries(s.values).map(([sourceValue, v]) => ({
    sourceValue,
    externalValueId: v.externalValueId,
    customValue: v.externalValueId === null && v.customValue.trim() ? v.customValue.trim() : null,
  }))
  return { ...base, sourceAttribute: s.sourceAttribute.trim(), fixedValueId: null, fixedCustomValue: null, values }
}
