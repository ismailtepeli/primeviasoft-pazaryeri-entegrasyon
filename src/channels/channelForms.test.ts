import { describe, expect, it } from 'vitest'
import { channelForms, formFor, INTEGRATOR_NAME, userAgentPreview, validate } from './channelForms'

describe('channelForms', () => {
  it('Trendyol: satıcı kimliği, anahtar, gizli anahtar ve ortam alanlarını ister', () => {
    expect(formFor('Trendyol')!.fields.map((f) => f.name)).toEqual(['sellerId', 'apiKey', 'apiSecret', 'environment'])
  })

  it('N11: yalnızca appKey ve appSecret', () => {
    expect(formFor('N11')!.fields.map((f) => f.name)).toEqual(['appKey', 'appSecret'])
  })

  it('Amazon için form yok (OAuth ile bağlanır)', () => {
    expect(formFor('Amazon')).toBeUndefined()
  })

  it('anahtar ve gizli anahtar alanları maskelidir', () => {
    const secrets = channelForms.flatMap((f) => f.fields).filter((f) => f.kind === 'secret')
    expect(secrets.map((f) => f.name)).toEqual(expect.arrayContaining(['apiKey', 'apiSecret', 'serviceKey', 'appKey', 'appSecret']))
  })

  it('boş zorunlu alanlar hata verir, ortam seçimi zorunlu sayılmaz', () => {
    const errors = validate(formFor('Trendyol')!, { sellerId: '12345', apiKey: '  ', environment: 'live' })
    expect(Object.keys(errors)).toEqual(['apiKey', 'apiSecret'])
  })

  it('tüm alanlar doluysa hata yok', () => {
    expect(validate(formFor('N11')!, { appKey: 'a', appSecret: 'b' })).toEqual({})
  })

  it('User-Agent önizlemesi satıcı kimliğiyle kurulur', () => {
    expect(userAgentPreview(formFor('Trendyol')!, { sellerId: '12345' })).toBe(`12345 - ${INTEGRATOR_NAME}`)
  })

  it('satıcı kimliği boşken alanın adını gösterir', () => {
    expect(userAgentPreview(formFor('Trendyol')!, {})).toBe(`{Satıcı ID} - ${INTEGRATOR_NAME}`)
  })

  it('User-Agent kullanmayan kanalda önizleme yok', () => {
    expect(userAgentPreview(formFor('N11')!, {})).toBeNull()
  })

  it('firma adı kanalların kuralına uyar: alfanumerik, en fazla 30 karakter', () => {
    expect(INTEGRATOR_NAME).toMatch(/^[A-Za-z0-9]{1,30}$/)
  })
})
