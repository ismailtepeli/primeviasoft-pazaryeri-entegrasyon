import { describe, expect, it } from 'vitest'
import { emptyApplyForm, validateApply, validateForgot, validateLogin, validateNewPassword, validatePasswordChange } from './validation'

describe('validation', () => {
  it('giriş formu e-posta ve parola ister', () => {
    expect(validateLogin('', '')).toEqual({ email: 'Geçerli bir e-posta girin.', password: 'Parola zorunlu.' })
    expect(validateLogin('a@b.co', 'x')).toEqual({})
  })

  it('başvuru formu zorunlu alanları denetler', () => {
    expect(Object.keys(validateApply(emptyApplyForm)).sort()).toEqual(['companyName', 'email', 'password', 'taxNumber'])
    const ok = { ...emptyApplyForm, companyName: 'Mavi', taxNumber: '1234567890', email: 'a@b.co', password: '12345678' }
    expect(validateApply(ok)).toEqual({})
    expect(validateApply({ ...ok, taxNumber: '12345678901' })).toEqual({})
    expect(validateApply({ ...ok, taxNumber: '123456789' })).toHaveProperty('taxNumber')
    expect(validateApply({ ...ok, password: '1234567' })).toHaveProperty('password')
  })

  it('parola sıfırlama isteği e-postayı denetler', () => {
    expect(validateForgot('a@b.co')).toEqual({})
    expect(validateForgot(' ')).toHaveProperty('email')
    expect(validateForgot('abc')).toHaveProperty('email')
  })

  it('yeni parola uzunluğu ve tekrarı denetlenir', () => {
    expect(validateNewPassword('12345678', '12345678')).toEqual({})
    expect(validateNewPassword('1234567', '1234567')).toHaveProperty('password')
    expect(validateNewPassword('12345678', '12345679')).toHaveProperty('confirm')
    expect(validateNewPassword('x'.repeat(101), 'x'.repeat(101))).toHaveProperty('password')
  })

  it('parola değiştirmede mevcut parola zorunlu ve yenisi farklı olmalı', () => {
    expect(validatePasswordChange('eski12345', 'yeni12345', 'yeni12345')).toEqual({})
    expect(validatePasswordChange('', 'yeni12345', 'yeni12345')).toHaveProperty('current')
    expect(validatePasswordChange('aynisi123', 'aynisi123', 'aynisi123')).toHaveProperty('password')
  })
})
