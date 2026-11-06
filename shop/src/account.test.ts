import { describe, expect, it } from 'vitest'
import {
  addressSummary, emptyAddress, formatDate, formatDateTime, orderStatusTone, orderSteps, pageCount, safeNext, validateAddress, validateEmail, validatePassword, validatePhone, validateRegister,
} from './account'

describe('account', () => {
  it('e-posta, parola ve telefonu doğrular', () => {
    expect(validateEmail('ayse@mail.com')).toBeNull()
    expect(validateEmail('ayse@mail')).not.toBeNull()
    expect(validateEmail('')).not.toBeNull()
    expect(validatePassword('1234567')).not.toBeNull()
    expect(validatePassword('12345678')).toBeNull()
    expect(validatePhone('', false)).toBeNull()
    expect(validatePhone('', true)).not.toBeNull()
    expect(validatePhone('0555 123 45 67', true)).toBeNull()
    expect(validatePhone('+90 (555) 123-45-67', true)).toBeNull()
    expect(validatePhone('abc', false)).not.toBeNull()
    expect(validatePhone('12345', false)).not.toBeNull()
  })

  it('kayıt formunda her hata ilgili alana yazılır; koşullar kabul edilmeden geçmez', () => {
    const ok = { fullName: 'Ayşe Yılmaz', email: 'ayse@mail.com', password: 'Parola123', phone: '', acceptTerms: true }
    expect(validateRegister(ok)).toEqual({})
    expect(Object.keys(validateRegister({ fullName: 'A', email: 'x', password: 'kisa', phone: '1', acceptTerms: false })).sort()).toEqual(['acceptTerms', 'email', 'fullName', 'password', 'phone'])
  })

  it('adres formunu doğrular ve özet satırı kurar', () => {
    const ok = { ...emptyAddress, title: 'Ev', fullName: 'Ayşe Yılmaz', phone: '0555 123 45 67', city: 'İstanbul', district: 'Kadıköy', addressLine: 'Moda Cad. No:5', postalCode: '34710' }
    expect(validateAddress(ok)).toEqual({})
    expect(Object.keys(validateAddress(emptyAddress)).sort()).toEqual(['addressLine', 'city', 'district', 'fullName', 'phone', 'title'])
    expect(validateAddress({ ...ok, postalCode: '12' })).toHaveProperty('postalCode')
    expect(validateAddress({ ...ok, postalCode: '' })).toEqual({})
    expect(addressSummary(ok)).toBe('Moda Cad. No:5, Kadıköy / İstanbul 34710')
  })

  it('sipariş durumları ve ilerleme adımları', () => {
    expect([orderStatusTone('Delivered'), orderStatusTone('Cancelled'), orderStatusTone('New'), orderStatusTone('Shipped')]).toEqual(['success', 'danger', 'info', 'progress'])
    expect(orderSteps('Preparing').map((s) => s.state)).toEqual(['done', 'current', 'todo', 'todo'])
    expect(orderSteps('Delivered').map((s) => s.state)).toEqual(['done', 'done', 'done', 'done'])
    expect(orderSteps('Cancelled')).toEqual([])
  })

  it('tarihi saat dilimi eki olmayan sunucu zamanını UTC sayarak yazar', () => {
    expect(formatDate('2026-11-02T10:00:00')).toMatch(/2 Kasım 2026/)
    expect(formatDateTime('2026-11-02T10:00:00Z')).toMatch(/2 Kasım 2026/)
  })

  it('giriş sonrası dönüş adresi yalnızca uygulama içi olabilir', () => {
    expect(safeNext('/hesap/siparisler')).toBe('/hesap/siparisler')
    expect(safeNext('https://evil.example')).toBe('/hesap')
    expect(safeNext('//evil.example')).toBe('/hesap')
    expect(safeNext('/\\evil')).toBe('/hesap')
    expect(safeNext(null)).toBe('/hesap')
  })

  it('sayfa sayısı', () => {
    expect([pageCount(0, 10), pageCount(10, 10), pageCount(11, 10), pageCount(5, 0)]).toEqual([1, 1, 2, 5])
  })
})
