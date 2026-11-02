import { describe, expect, it } from 'vitest'
import { toForm, validateSettings, type B2CSettings, type B2CSettingsForm } from './b2cSettings'

const settings: B2CSettings = { shippingFee: 49.9, freeShippingThreshold: 500, bankTransferEnabled: true, bankTransferInfo: 'IBAN TR00', cashOnDeliveryEnabled: false, cashOnDeliveryFee: 0, cardEnabled: false, cardProviderAvailable: false }
const form = (patch: Partial<B2CSettingsForm> = {}): B2CSettingsForm => ({ ...toForm(settings), ...patch })

describe('B2C ayar formu', () => {
  it('sunucudaki ayarı Türkçe sayı biçimiyle forma çevirir', () => {
    expect(toForm(settings)).toMatchObject({ shippingFee: '49,9', freeShippingThreshold: '500', bankTransferInfo: 'IBAN TR00' })
    expect(toForm({ ...settings, freeShippingThreshold: null, bankTransferInfo: null }).freeShippingThreshold).toBe('')
  })

  it('geçerli formu API gövdesine çevirir; boş eşik ücretsiz kargoyu kapatır', () => {
    expect(validateSettings(form())).toEqual({ body: { shippingFee: 49.9, freeShippingThreshold: 500, bankTransferEnabled: true, bankTransferInfo: 'IBAN TR00', cashOnDeliveryEnabled: false, cashOnDeliveryFee: 0, cardEnabled: false } })
    expect(validateSettings(form({ freeShippingThreshold: ' ', shippingFee: '1.250,50' }))).toMatchObject({ body: { freeShippingThreshold: null, shippingFee: 1250.5 } })
  })

  it('kargo, eşik ve kapıda ödeme ücretini doğrular', () => {
    expect(validateSettings(form({ shippingFee: 'abc' }))).toHaveProperty('error')
    expect(validateSettings(form({ shippingFee: '-1' }))).toHaveProperty('error')
    expect(validateSettings(form({ shippingFee: '100001' }))).toHaveProperty('error')
    expect(validateSettings(form({ freeShippingThreshold: '0' }))).toHaveProperty('error')
    expect(validateSettings(form({ cashOnDeliveryEnabled: true, cashOnDeliveryFee: 'x' }))).toHaveProperty('error')
  })

  it('havale açıkken bilgi zorunlu, hiçbir ödeme yöntemi açık değilse kayıt reddedilir', () => {
    expect(validateSettings(form({ bankTransferInfo: '   ' }))).toHaveProperty('error')
    expect(validateSettings(form({ bankTransferInfo: 'x'.repeat(1001) }))).toHaveProperty('error')
    expect(validateSettings(form({ bankTransferEnabled: false, bankTransferInfo: '' }))).toHaveProperty('error')
    expect(validateSettings(form({ bankTransferEnabled: false, bankTransferInfo: '', cashOnDeliveryEnabled: true }))).toHaveProperty('body')
  })
})
