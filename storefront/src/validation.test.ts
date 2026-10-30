import { describe, expect, it } from 'vitest'
import { emptyApplyForm, validateApply, validateLogin } from './validation'

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
})
