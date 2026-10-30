import { describe, expect, it } from 'vitest'
import { errorsToCsv, importSummary } from './importReport'

describe('importReport', () => {
  it('hata raporunu CSV yapar, tırnakları kaçırır ve BOM koyar', () => {
    const csv = errorsToCsv([{ row: 3, barcode: 'B"1', message: 'Ürün adı boş.' }])
    expect(csv.startsWith('﻿"Satır";"Barkod";"Hata"')).toBe(true)
    expect(csv).toContain('"3";"B""1";"Ürün adı boş."')
  })

  it('özet cümlesi yalnızca anlamlı parçaları içerir', () => {
    expect(importSummary({ created: 2, updated: 1, groups: 3, errors: [] })).toBe('2 yeni ürün eklendi, 1 ürün güncellendi.')
    expect(importSummary({ created: 4, updated: 0, groups: 2, errors: [1] })).toBe('4 yeni ürün eklendi, 0 ürün güncellendi, 2 ürün grubunda toplandı, 1 satır hatalı.')
  })
})
