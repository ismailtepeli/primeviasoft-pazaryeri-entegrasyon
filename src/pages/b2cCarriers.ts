import { parsePriceText } from './priceEdit'

export interface CarrierRate { upToDesi: number; fee: number }

export interface Carrier {
  id: string
  name: string
  enabled: boolean
  flatFee: number
  trackingUrlTemplate: string | null
  rates: CarrierRate[]
}

/** Formdaki metin alanları; sayılar kullanıcının yazdığı biçimde ("1.250,50") tutulur. */
export interface CarrierForm {
  id: string | null
  name: string
  enabled: boolean
  flatFee: string
  trackingUrlTemplate: string
  rates: { upToDesi: string; fee: string }[]
}

export const MAX_NAME = 60
export const MAX_URL = 300
export const MAX_RATES = 20
export const MAX_FEE = 100_000
export const TRACKING_PLACEHOLDER = '{takipNo}'

const money = (n: number) => String(n).replace('.', ',')

export const emptyForm = (): CarrierForm => ({ id: null, name: '', enabled: true, flatFee: '', trackingUrlTemplate: '', rates: [] })

export const toForm = (c: Carrier): CarrierForm => ({
  id: c.id, name: c.name, enabled: c.enabled, flatFee: money(c.flatFee), trackingUrlTemplate: c.trackingUrlTemplate ?? '',
  rates: c.rates.map((r) => ({ upToDesi: money(r.upToDesi), fee: money(r.fee) })),
})

export interface CarrierBody { name: string; enabled: boolean; flatFee: number; trackingUrlTemplate: string | null; rates: CarrierRate[] }

/** Sunucudaki kurallarla aynı denetim; geçerliyse API gövdesi, değilse hata metni. Kural varsa sabit ücret kullanılmaz (0 gönderilir). */
export function validateCarrier(f: CarrierForm, otherNames: string[]): { body: CarrierBody } | { error: string } {
  const name = f.name.trim()
  if (!name || name.length > MAX_NAME) return { error: `Firma adı 1 ile ${MAX_NAME} karakter arasında olmalı.` }
  if (otherNames.some((n) => n.trim().toLocaleLowerCase('tr') === name.toLocaleLowerCase('tr'))) return { error: 'Bu adda bir kargo firması zaten var.' }

  const rates: CarrierRate[] = []
  for (const r of f.rates) {
    const desi = parsePriceText(r.upToDesi)
    const fee = parsePriceText(r.fee)
    if (desi === null || desi <= 0 || desi > MAX_FEE) return { error: 'Desi sınırı sıfırdan büyük bir sayı olmalı.' }
    if (fee === null || fee < 0 || fee > MAX_FEE) return { error: 'Aralık ücreti 0 ile 100.000 arasında olmalı.' }
    rates.push({ upToDesi: desi, fee })
  }
  if (rates.length > MAX_RATES) return { error: `En fazla ${MAX_RATES} desi aralığı tanımlanabilir.` }
  if (new Set(rates.map((r) => r.upToDesi)).size !== rates.length) return { error: 'Aynı desi sınırı iki kez yazılamaz.' }

  let flat = 0
  if (rates.length === 0) {
    const parsed = parsePriceText(f.flatFee)
    if (parsed === null || parsed < 0 || parsed > MAX_FEE) return { error: 'Kargo ücreti 0 ile 100.000 arasında bir tutar olmalı.' }
    flat = parsed
  }

  const template = f.trackingUrlTemplate.trim()
  if (template && (template.length > MAX_URL || !/^https:\/\//i.test(template) || !template.includes(TRACKING_PLACEHOLDER)))
    return { error: `Takip bağlantısı https ile başlamalı ve ${TRACKING_PLACEHOLDER} içermeli.` }

  return { body: { name, enabled: f.enabled, flatFee: flat, trackingUrlTemplate: template || null, rates: rates.sort((a, b) => a.upToDesi - b.upToDesi) } }
}

const tl = (n: number) => `${n.toLocaleString('tr-TR', { minimumFractionDigits: 0, maximumFractionDigits: 2 })} ₺`

/** Listedeki özet: sabit ücret ya da desi aralıkları. */
export function carrierSummary(c: Carrier): string {
  if (c.rates.length === 0) return c.flatFee === 0 ? 'Sabit ücret: ücretsiz' : `Sabit ücret: ${tl(c.flatFee)}`
  const sorted = [...c.rates].sort((a, b) => a.upToDesi - b.upToDesi)
  return `${sorted.length} desi aralığı · ${tl(sorted[0].fee)} – ${tl(sorted[sorted.length - 1].fee)} · en çok ${sorted[sorted.length - 1].upToDesi.toLocaleString('tr-TR')} desi`
}
