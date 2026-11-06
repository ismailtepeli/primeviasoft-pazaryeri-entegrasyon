import { parsePriceText } from './priceEdit'

export interface B2CSettings {
  shippingFee: number
  freeShippingThreshold: number | null
  bankTransferEnabled: boolean
  bankTransferInfo: string | null
  cashOnDeliveryEnabled: boolean
  cashOnDeliveryFee: number
  cardEnabled: boolean
  /** Havale siparişinin ödeme bekleme süresi (saat, 24–72). */
  bankTransferHoldHours: number
  /** Sunucuda bir ödeme sağlayıcısı yapılandırılmış mı; değilse kart ödemesi açılamaz. */
  cardProviderAvailable: boolean
}

/** Formdaki metin alanları; sayılar kullanıcının yazdığı biçimde ("1.250,50") tutulur. */
export interface B2CSettingsForm {
  shippingFee: string
  freeShippingThreshold: string
  bankTransferEnabled: boolean
  bankTransferInfo: string
  cashOnDeliveryEnabled: boolean
  cashOnDeliveryFee: string
  cardEnabled: boolean
  bankTransferHoldHours: string
}

const money = (n: number) => String(n).replace('.', ',')

export function toForm(s: B2CSettings): B2CSettingsForm {
  return {
    shippingFee: money(s.shippingFee), freeShippingThreshold: s.freeShippingThreshold === null ? '' : money(s.freeShippingThreshold),
    bankTransferEnabled: s.bankTransferEnabled, bankTransferInfo: s.bankTransferInfo ?? '',
    cashOnDeliveryEnabled: s.cashOnDeliveryEnabled, cashOnDeliveryFee: money(s.cashOnDeliveryFee), cardEnabled: s.cardEnabled,
    bankTransferHoldHours: String(s.bankTransferHoldHours),
  }
}

export const MAX_INFO = 1000
export const MIN_HOLD_HOURS = 24
export const MAX_HOLD_HOURS = 72
const MAX_FEE = 100_000

/** Sunucudaki kurallarla aynı denetim; geçerliyse API gövdesi, değilse hata metni. */
export function validateSettings(f: B2CSettingsForm): { body: Omit<B2CSettings, 'cardProviderAvailable'> } | { error: string } {
  const fee = parsePriceText(f.shippingFee)
  if (fee === null || fee < 0 || fee > MAX_FEE) return { error: 'Kargo ücreti 0 ile 100.000 arasında bir tutar olmalı.' }
  let threshold: number | null = null
  if (f.freeShippingThreshold.trim() !== '') {
    threshold = parsePriceText(f.freeShippingThreshold)
    if (threshold === null || threshold <= 0) return { error: 'Ücretsiz kargo tutarı sıfırdan büyük olmalı; kapatmak için boş bırakın.' }
  }
  const cod = f.cashOnDeliveryEnabled ? parsePriceText(f.cashOnDeliveryFee) : (parsePriceText(f.cashOnDeliveryFee) ?? 0)
  if (cod === null || cod < 0 || cod > MAX_FEE) return { error: 'Kapıda ödeme ücreti 0 ile 100.000 arasında bir tutar olmalı.' }
  const info = f.bankTransferInfo.trim()
  if (info.length > MAX_INFO) return { error: `Havale bilgisi en fazla ${MAX_INFO} karakter olabilir.` }
  const hold = Number(f.bankTransferHoldHours)
  if (!Number.isInteger(hold) || hold < MIN_HOLD_HOURS || hold > MAX_HOLD_HOURS) return { error: `Havale bekleme süresi ${MIN_HOLD_HOURS} ile ${MAX_HOLD_HOURS} saat arasında tam sayı olmalı.` }
  if (f.bankTransferEnabled && info === '') return { error: 'Havale açıksa müşterinin göreceği banka/IBAN bilgisini yazın.' }
  if (!f.bankTransferEnabled && !f.cashOnDeliveryEnabled && !f.cardEnabled) return { error: 'En az bir ödeme yöntemi açık olmalı; yoksa müşteri sipariş veremez.' }
  return { body: { shippingFee: fee, freeShippingThreshold: threshold, bankTransferEnabled: f.bankTransferEnabled, bankTransferInfo: info === '' ? null : info, cashOnDeliveryEnabled: f.cashOnDeliveryEnabled, cashOnDeliveryFee: cod, cardEnabled: f.cardEnabled, bankTransferHoldHours: hold } }
}
