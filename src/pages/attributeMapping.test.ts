import { describe, expect, it } from 'vitest'
import type { AttributeRow } from '../api/types'
import { buildSaveBody, initialEditor, mappingSummary } from './attributeMapping'

const row = (over: Partial<AttributeRow> = {}): AttributeRow => ({
  attributeId: 47, name: 'Renk', required: true, allowCustom: false, varianter: true, valueCount: 3, status: 'Missing', mapping: null, ...over,
})

const sourceMapping = { sourceAttribute: 'Renk', fixedValueId: null, fixedValueName: null, fixedCustomValue: null, values: [{ sourceValue: 'Mavi', externalValueId: 2, externalValueName: 'Mavi', customValue: null }] }

describe('attributeMapping', () => {
  it('eşleme özetini sabit ve kaynak için ayrı yazar', () => {
    expect(mappingSummary(row())).toBe('—')
    expect(mappingSummary(row({ mapping: { sourceAttribute: null, fixedValueId: 70, fixedValueName: 'M', fixedCustomValue: null, values: [] } }))).toBe('Sabit: M')
    expect(mappingSummary(row({ mapping: sourceMapping }))).toBe('Ürünün "Renk" özelliğinden (1 değer eşli)')
  })

  it('düzenleyici ürünlerde gördüğümüz değerleri ve kayıtlı eşlemeyi birleştirir', () => {
    const state = initialEditor(row({ mapping: sourceMapping }), () => ['Kırmızı', 'Mavi'])
    expect(state.mode).toBe('source')
    expect(state.values).toEqual({ Kırmızı: { externalValueId: null, customValue: '' }, Mavi: { externalValueId: 2, customValue: '' } })
  })

  it('sabit değer gövdesini kurar ve boşsa hata verir', () => {
    const s = initialEditor(row(), () => [])
    expect(typeof buildSaveBody('Trendyol', 'c1', row(), s)).toBe('string')
    expect(buildSaveBody('Trendyol', 'c1', row(), { ...s, fixedValueId: 70 })).toMatchObject({ fixedValueId: 70, fixedCustomValue: null, sourceAttribute: null })
    expect(buildSaveBody('Trendyol', 'c1', row(), { ...s, fixedCustomValue: ' Pamuk ' })).toMatchObject({ fixedValueId: null, fixedCustomValue: 'Pamuk' })
  })

  it('kaynak özellik gövdesinde listeyi ya da serbest metni gönderir', () => {
    const s = { ...initialEditor(row(), () => ['Mavi', 'Yeşil']), mode: 'source' as const, sourceAttribute: 'Renk' }
    s.values.Mavi.externalValueId = 2
    s.values.Yeşil.customValue = ' Zümrüt '
    const body = buildSaveBody('Trendyol', 'c1', row(), s)
    expect(body).toMatchObject({ sourceAttribute: 'Renk', fixedValueId: null })
    expect((body as { values: unknown[] }).values).toEqual([
      { sourceValue: 'Mavi', externalValueId: 2, customValue: null },
      { sourceValue: 'Yeşil', externalValueId: null, customValue: 'Zümrüt' },
    ])
    expect(typeof buildSaveBody('Trendyol', 'c1', row(), { ...s, sourceAttribute: ' ' })).toBe('string')
  })
})
