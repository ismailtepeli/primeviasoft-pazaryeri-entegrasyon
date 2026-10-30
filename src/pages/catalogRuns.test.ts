import { describe, expect, it } from 'vitest'
import { runSummary } from './catalogRuns'

describe('runSummary', () => {
  it('yalnızca sıfırdan büyük değişiklikleri yazar', () => {
    expect(runSummary({ added: 3, changed: 0, removed: 1, error: null })).toBe('3 yeni, 1 kaldırıldı')
    expect(runSummary({ added: 0, changed: 2, removed: 0, error: null })).toBe('2 değişti')
  })

  it('değişiklik yoksa ve hata varsa uygun metin verir', () => {
    expect(runSummary({ added: 0, changed: 0, removed: 0, error: null })).toBe('Değişiklik yok')
    expect(runSummary({ added: 5, changed: 0, removed: 0, error: 'boş döndü' })).toBe('Hata: boş döndü')
  })
})
