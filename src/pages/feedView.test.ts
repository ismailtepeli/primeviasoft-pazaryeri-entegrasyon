import { describe, expect, it } from 'vitest'
import { emptyForm, feedStatus, intervalLabel, missingRoles, runSummary, setRolePath, toForm, toggleImagePath, validateFeed, type FeedAnalysis, type ProductFeed } from './feedView'

const analysis: Pick<FeedAnalysis, 'itemPath' | 'mapping' | 'missing'> = { itemPath: 'Urunler/Urun', mapping: { barcode: ['Barkod'], name: ['UrunAdi'], price: ['Fiyat'] }, missing: [] }
const form = (patch = {}) => ({ ...emptyForm(), name: 'Tedarikçi A', url: 'https://t.example.com/feed.xml', ...patch })

const feed: ProductFeed = {
  id: 'f1', name: 'A', urlDisplay: 'https://t.example.com/feed.xml?…', enabled: true, itemPath: 'Urunler/Urun', mapping: {}, format: 'Genel XML', markupPercent: 12.5, priceIncludesVat: true,
  defaultVatRate: 20, availableStock: 10, syncPrice: true, syncStock: true, zeroMissing: true, publishB2C: false, publishB2B: false, intervalMinutes: 60, lastRunAt: null, running: false,
  lastStatus: null, lastCreated: 0, lastUpdated: 0, lastSkipped: 0, lastFailed: 0, lastError: null, lastErrors: [], productCount: 0,
}

describe('besleme formu', () => {
  it('kayıtlı beslemeyi forma çevirir; adres gizli olduğu için boş gelir', () => {
    expect(toForm(feed)).toMatchObject({ name: 'A', url: '', markup: '12,5', intervalMinutes: 60 })
  })

  it('geçerli formu API gövdesine çevirir; çözümleme varsa öğe yolu ve eşleme eklenir', () => {
    const r = validateFeed(form({ markup: '25,5' }), analysis, false)
    expect(r).toMatchObject({ body: { name: 'Tedarikçi A', url: 'https://t.example.com/feed.xml', itemPath: 'Urunler/Urun', mapping: analysis.mapping, markupPercent: 25.5 } })
  })

  it('düzenlemede çözümleme yoksa öğe yolu ve eşleme gönderilmez, adres boşsa null gider', () => {
    const r = validateFeed(form({ url: '' }), null, true)
    expect(r).toMatchObject({ body: { url: null, itemPath: null, mapping: null } })
  })

  it('yeni beslemede adres zorunlu, ad zorunlu', () => {
    expect(validateFeed(form({ url: ' ' }), analysis, false)).toHaveProperty('error')
    expect(validateFeed(form({ name: ' ' }), analysis, false)).toHaveProperty('error')
    expect(validateFeed(form({ name: 'x'.repeat(101) }), analysis, false)).toHaveProperty('error')
  })

  it('eşlenmemiş zorunlu alan kaydı engeller ve adıyla bildirir', () => {
    const r = validateFeed(form(), { ...analysis, missing: ['price', 'name'] }, false)
    expect(r).toEqual({ error: 'Zorunlu alan eşlenmedi: Fiyat, Ürün adı.' })
  })

  it('kâr yüzdesi, KDV ve stok sınırlarını doğrular', () => {
    expect(validateFeed(form({ markup: 'abc' }), analysis, false)).toHaveProperty('error')
    expect(validateFeed(form({ markup: '-91' }), analysis, false)).toHaveProperty('error')
    expect(validateFeed(form({ markup: '1001' }), analysis, false)).toHaveProperty('error')
    expect(validateFeed(form({ markup: '-10' }), analysis, false)).toHaveProperty('body')
    expect(validateFeed(form({ defaultVat: '101' }), analysis, false)).toHaveProperty('error')
    expect(validateFeed(form({ defaultVat: '7,5' }), analysis, false)).toHaveProperty('error')
    expect(validateFeed(form({ availableStock: '-1' }), analysis, false)).toHaveProperty('error')
    expect(validateFeed(form({ availableStock: '100001' }), analysis, false)).toHaveProperty('error')
  })
})

describe('eşleme düzenleme', () => {
  it('rol için tek yol seçer, boş seçim rolü kaldırır; özgün eşlemeyi değiştirmez', () => {
    const m = { barcode: ['A'] }
    expect(setRolePath(m, 'price', 'Fiyat')).toEqual({ barcode: ['A'], price: ['Fiyat'] })
    expect(setRolePath({ ...m, price: ['Fiyat'] }, 'price', '')).toEqual({ barcode: ['A'] })
    expect(m).toEqual({ barcode: ['A'] })
  })

  it('görsel alanlarını ekler, tekrarı yok sayar, hepsi kalkınca rolü siler', () => {
    let m = toggleImagePath({}, 'Resim1', true)
    m = toggleImagePath(toggleImagePath(m, 'Resim2', true), 'Resim2', true)
    expect(m.images).toEqual(['Resim1', 'Resim2'])
    m = toggleImagePath(toggleImagePath(m, 'Resim1', false), 'Resim2', false)
    expect(m).toEqual({})
  })

  it('eksik zorunlu rolleri bulur', () => {
    expect(missingRoles({ barcode: ['B'], price: [] })).toEqual(['name', 'price'])
    expect(missingRoles(analysis.mapping)).toEqual([])
  })
})

describe('liste görünümü', () => {
  it('durum rozeti: çalışıyor > kapalı > bekliyor > son sonuç', () => {
    expect(feedStatus({ ...feed, running: true })).toEqual({ text: 'Çalışıyor', variant: 'info' })
    expect(feedStatus({ ...feed, enabled: false, lastStatus: 'Success' }).text).toBe('Kapalı')
    expect(feedStatus(feed).text).toBe('Bekliyor')
    expect(feedStatus({ ...feed, lastStatus: 'Success' })).toEqual({ text: 'Başarılı', variant: 'success' })
    expect(feedStatus({ ...feed, lastStatus: 'Partial' }).variant).toBe('warning')
    expect(feedStatus({ ...feed, lastStatus: 'Failed' }).variant).toBe('error')
  })

  it('son çalışma özetinde yalnızca sıfırdan farklı sayılar yazılır', () => {
    expect(runSummary({ created: 12, updated: 340, skipped: 0, failed: 1, zeroedMissing: 2 })).toBe('12 yeni · 340 güncellendi · 1 hatalı · 2 ürünün stoğu sıfırlandı')
    expect(runSummary({ created: 0, updated: 0, skipped: 0, failed: 0 })).toBe('Değişiklik yok')
  })

  it('aralık etiketi', () => {
    expect(intervalLabel(60)).toBe('Saatte bir')
    expect(intervalLabel(45)).toBe('45 dakikada bir')
  })
})
