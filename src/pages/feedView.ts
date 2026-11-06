import { parsePriceText } from './priceEdit'

/** Besleme alanlarının sistemdeki karşılıkları; sunucudaki FeedRoles ile aynı. */
export const feedRoles = ['barcode', 'name', 'price', 'stock', 'brand', 'category', 'images', 'description', 'vat', 'mainId'] as const
export type FeedRole = (typeof feedRoles)[number]
export const requiredRoles: FeedRole[] = ['barcode', 'name', 'price']

export const roleLabel: Record<FeedRole, string> = {
  barcode: 'Barkod / ürün kodu', name: 'Ürün adı', price: 'Fiyat', stock: 'Stok', brand: 'Marka', category: 'Kategori',
  images: 'Görseller', description: 'Açıklama', vat: 'KDV oranı', mainId: 'Ürün ana kodu (varyant grubu)',
}

export const roleHint: Partial<Record<FeedRole, string>> = {
  barcode: 'Ürünü tanıyan benzersiz kod; stok/fiyat güncellemesi buna göre yapılır',
  price: 'Tedarikçinin sizden istediği fiyat (maliyetiniz)',
  stock: 'Sayı ya da "stokta var/yok" metni olabilir',
  images: 'Birden çok alan seçebilirsiniz (en çok 8 görsel)',
}

export type FeedMapping = Partial<Record<FeedRole, string[]>>

export interface FeedField { path: string; samples: string[]; repeated: boolean }
export interface FeedCandidate { path: string; count: number; score: number }
export interface FeedPreviewRow { barcode: string | null; name: string | null; price: number | null; stock: number | null; brand: string | null; category: string | null; imageCount: number; problems: string[] }

export interface FeedAnalysis {
  format: string
  itemPath: string
  itemCount: number
  candidates: FeedCandidate[]
  fields: FeedField[]
  mapping: FeedMapping
  missing: FeedRole[]
  preview: FeedPreviewRow[]
}

export interface FeedRowError { item: number; key: string; message: string }

export interface ProductFeed {
  id: string
  name: string
  urlDisplay: string
  enabled: boolean
  itemPath: string
  mapping: FeedMapping
  format: string | null
  markupPercent: number
  priceIncludesVat: boolean
  defaultVatRate: number
  availableStock: number
  syncPrice: boolean
  syncStock: boolean
  zeroMissing: boolean
  publishB2C: boolean
  publishB2B: boolean
  intervalMinutes: number
  lastRunAt: string | null
  running: boolean
  lastStatus: 'Success' | 'Partial' | 'Failed' | null
  lastCreated: number
  lastUpdated: number
  lastSkipped: number
  lastFailed: number
  lastError: string | null
  lastErrors: FeedRowError[]
  productCount: number
}

export interface FeedRunResult { status: string; created: number; updated: number; skipped: number; failed: number; zeroedMissing: number; errors: FeedRowError[] }

/** Formdaki metin alanları; sayılar kullanıcının yazdığı biçimde ("12,5") tutulur. */
export interface FeedForm {
  name: string
  url: string
  enabled: boolean
  markup: string
  priceIncludesVat: boolean
  defaultVat: string
  availableStock: string
  syncPrice: boolean
  syncStock: boolean
  zeroMissing: boolean
  publishB2C: boolean
  publishB2B: boolean
  intervalMinutes: number
}

export const intervalOptions = [
  { value: 15, label: '15 dakikada bir' }, { value: 30, label: '30 dakikada bir' }, { value: 60, label: 'Saatte bir' }, { value: 180, label: '3 saatte bir' },
  { value: 360, label: '6 saatte bir' }, { value: 720, label: '12 saatte bir' }, { value: 1440, label: 'Günde bir' },
]

export const emptyForm = (): FeedForm => ({
  name: '', url: '', enabled: true, markup: '20', priceIncludesVat: true, defaultVat: '20', availableStock: '10',
  syncPrice: true, syncStock: true, zeroMissing: true, publishB2C: false, publishB2B: false, intervalMinutes: 60,
})

const num = (n: number) => String(n).replace('.', ',')

export const toForm = (f: ProductFeed): FeedForm => ({
  name: f.name, url: '', enabled: f.enabled, markup: num(f.markupPercent), priceIncludesVat: f.priceIncludesVat, defaultVat: String(f.defaultVatRate),
  availableStock: String(f.availableStock), syncPrice: f.syncPrice, syncStock: f.syncStock, zeroMissing: f.zeroMissing, publishB2C: f.publishB2C, publishB2B: f.publishB2B,
  intervalMinutes: f.intervalMinutes,
})

export interface FeedBody {
  name: string
  url: string | null
  enabled: boolean
  itemPath: string | null
  mapping: FeedMapping | null
  markupPercent: number
  priceIncludesVat: boolean
  defaultVatRate: number
  availableStock: number
  syncPrice: boolean
  syncStock: boolean
  zeroMissing: boolean
  publishB2C: boolean
  publishB2B: boolean
  intervalMinutes: number
}

/** Sunucudaki kurallarla aynı denetim; geçerliyse API gövdesi, değilse hata metni. Çözümleme yoksa (düzenleme) öğe yolu ve eşleme gönderilmez, kayıtlı olan korunur. */
export function validateFeed(f: FeedForm, analysis: Pick<FeedAnalysis, 'itemPath' | 'mapping' | 'missing'> | null, editing: boolean): { body: FeedBody } | { error: string } {
  const name = f.name.trim()
  if (!name || name.length > 100) return { error: 'Besleme adı 1 ile 100 karakter arasında olmalı.' }
  if (!editing && !f.url.trim()) return { error: 'Besleme adresini yazın.' }
  if (analysis && analysis.missing.length > 0) return { error: `Zorunlu alan eşlenmedi: ${analysis.missing.map((r) => roleLabel[r]).join(', ')}.` }
  const markup = parsePriceText(f.markup)
  if (markup === null || markup < -90 || markup > 1000) return { error: 'Kâr yüzdesi -90 ile 1000 arasında bir sayı olmalı.' }
  const vat = Number(f.defaultVat)
  if (!Number.isInteger(vat) || vat < 0 || vat > 100) return { error: 'Varsayılan KDV oranı 0 ile 100 arasında tam sayı olmalı.' }
  const stock = Number(f.availableStock)
  if (!Number.isInteger(stock) || stock < 0 || stock > 100_000) return { error: 'Stokta var adedi 0 ile 100.000 arasında tam sayı olmalı.' }
  return {
    body: {
      name, url: f.url.trim() || null, enabled: f.enabled, itemPath: analysis?.itemPath ?? null, mapping: analysis?.mapping ?? null,
      markupPercent: markup, priceIncludesVat: f.priceIncludesVat, defaultVatRate: vat, availableStock: stock, syncPrice: f.syncPrice, syncStock: f.syncStock,
      zeroMissing: f.zeroMissing, publishB2C: f.publishB2C, publishB2B: f.publishB2B, intervalMinutes: f.intervalMinutes,
    },
  }
}

/** Eşlemede bir rolün tek yolunu değiştirir; boş yol rolü kaldırır. Görsel rolü için {@link toggleImagePath} kullanılır. */
export function setRolePath(mapping: FeedMapping, role: FeedRole, path: string): FeedMapping {
  const next = { ...mapping }
  if (path) next[role] = [path]
  else delete next[role]
  return next
}

/** Görsel alanını ekler ya da çıkarır; hiç kalmazsa rol kaldırılır. */
export function toggleImagePath(mapping: FeedMapping, path: string, on: boolean): FeedMapping {
  const current = mapping.images ?? []
  const paths = on ? [...new Set([...current, path])] : current.filter((p) => p !== path)
  const next = { ...mapping }
  if (paths.length > 0) next.images = paths
  else delete next.images
  return next
}

export function missingRoles(mapping: FeedMapping): FeedRole[] {
  return requiredRoles.filter((r) => !mapping[r] || mapping[r]!.length === 0)
}

export const statusInfo: Record<string, { text: string; variant: 'success' | 'warning' | 'error' | 'inactive' }> = {
  Success: { text: 'Başarılı', variant: 'success' },
  Partial: { text: 'Kısmen', variant: 'warning' },
  Failed: { text: 'Hata', variant: 'error' },
}

/** Listedeki durum rozeti: çalışıyorsa "Çalışıyor", hiç çalışmadıysa "Bekliyor". */
export function feedStatus(f: Pick<ProductFeed, 'running' | 'lastStatus' | 'enabled'>): { text: string; variant: 'success' | 'warning' | 'error' | 'inactive' | 'info' } {
  if (f.running) return { text: 'Çalışıyor', variant: 'info' }
  if (!f.enabled) return { text: 'Kapalı', variant: 'inactive' }
  if (!f.lastStatus) return { text: 'Bekliyor', variant: 'inactive' }
  return statusInfo[f.lastStatus] ?? { text: f.lastStatus, variant: 'inactive' }
}

/** "12 yeni · 340 güncellendi · 2 atlandı · 1 hatalı" gibi son çalışma özeti; sıfır olanlar yazılmaz. */
export function runSummary(r: { created: number; updated: number; skipped: number; failed: number; zeroedMissing?: number }): string {
  const parts = [
    r.created && `${r.created} yeni`, r.updated && `${r.updated} güncellendi`, r.skipped && `${r.skipped} atlandı`, r.failed && `${r.failed} hatalı`,
    r.zeroedMissing && `${r.zeroedMissing} ürünün stoğu sıfırlandı`,
  ].filter(Boolean)
  return parts.length === 0 ? 'Değişiklik yok' : parts.join(' · ')
}

export function intervalLabel(minutes: number): string {
  return intervalOptions.find((o) => o.value === minutes)?.label ?? `${minutes} dakikada bir`
}
