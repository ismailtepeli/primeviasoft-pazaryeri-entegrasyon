import { describe, expect, it } from 'vitest'
import { carrierSummary, emptyForm, toForm, validateCarrier, type Carrier, type CarrierForm } from './b2cCarriers'

const carrier: Carrier = { id: 'c1', name: 'Yurtiçi', enabled: true, flatFee: 45, trackingUrlTemplate: 'https://t.example.com/?no={takipNo}', rates: [{ upToDesi: 10, fee: 60 }, { upToDesi: 5, fee: 40 }] }
const form = (patch: Partial<CarrierForm> = {}): CarrierForm => ({ ...emptyForm(), name: 'Aras', flatFee: '35', ...patch })

describe('kargo firması formu', () => {
  it('sunucudaki firmayı Türkçe sayı biçimiyle forma çevirir', () => {
    expect(toForm({ ...carrier, flatFee: 45.5 })).toMatchObject({ id: 'c1', flatFee: '45,5', rates: [{ upToDesi: '10', fee: '60' }, { upToDesi: '5', fee: '40' }] })
  })

  it('geçerli formu API gövdesine çevirir; kural yoksa sabit ücret, kural varsa 0 ve sıralı aralıklar gider', () => {
    expect(validateCarrier(form(), [])).toEqual({ body: { name: 'Aras', enabled: true, flatFee: 35, trackingUrlTemplate: null, rates: [] } })
    expect(validateCarrier(form({ flatFee: 'x', rates: [{ upToDesi: '10', fee: '60' }, { upToDesi: '5', fee: '40' }] }), []))
      .toEqual({ body: { name: 'Aras', enabled: true, flatFee: 0, trackingUrlTemplate: null, rates: [{ upToDesi: 5, fee: 40 }, { upToDesi: 10, fee: 60 }] } })
  })

  it('ad zorunlu, aynı ad (büyük/küçük harf ve Türkçe i fark etmez) tekrar edilemez', () => {
    expect(validateCarrier(form({ name: '  ' }), [])).toHaveProperty('error')
    expect(validateCarrier(form({ name: 'x'.repeat(61) }), [])).toHaveProperty('error')
    expect(validateCarrier(form({ name: 'YURTİÇİ' }), ['Yurtiçi'])).toHaveProperty('error')
  })

  it('ücret ve desi sınırlarını doğrular', () => {
    expect(validateCarrier(form({ flatFee: 'abc' }), [])).toHaveProperty('error')
    expect(validateCarrier(form({ flatFee: '-1' }), [])).toHaveProperty('error')
    expect(validateCarrier(form({ rates: [{ upToDesi: '0', fee: '10' }] }), [])).toHaveProperty('error')
    expect(validateCarrier(form({ rates: [{ upToDesi: '5', fee: '-2' }] }), [])).toHaveProperty('error')
    expect(validateCarrier(form({ rates: [{ upToDesi: '5', fee: '10' }, { upToDesi: '5', fee: '20' }] }), [])).toHaveProperty('error')
    expect(validateCarrier(form({ rates: Array.from({ length: 21 }, (_, i) => ({ upToDesi: String(i + 1), fee: '1' })) }), [])).toHaveProperty('error')
  })

  it('takip bağlantısı https olmalı ve {takipNo} içermeli', () => {
    expect(validateCarrier(form({ trackingUrlTemplate: 'https://t.example.com/{takipNo}' }), [])).toHaveProperty('body')
    expect(validateCarrier(form({ trackingUrlTemplate: 'http://t.example.com/{takipNo}' }), [])).toHaveProperty('error')
    expect(validateCarrier(form({ trackingUrlTemplate: 'https://t.example.com/' }), [])).toHaveProperty('error')
    expect(validateCarrier(form({ trackingUrlTemplate: 'javascript:alert({takipNo})' }), [])).toHaveProperty('error')
  })

  it('listede sabit ücreti ya da desi aralıklarını özetler', () => {
    expect(carrierSummary({ ...carrier, rates: [] })).toBe('Sabit ücret: 45 ₺')
    expect(carrierSummary({ ...carrier, rates: [], flatFee: 0 })).toBe('Sabit ücret: ücretsiz')
    expect(carrierSummary(carrier)).toBe('2 desi aralığı · 40 ₺ – 60 ₺ · en çok 10 desi')
  })
})
